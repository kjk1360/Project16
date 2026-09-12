using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kylin.DI;
using Kylin.DI.Layered;
using MessagePack;
using MessagePack.Formatters;
using NUnit.Framework;
using Project16.CardGame.Domain;
using Project16.CardGame.Persistence;
using Project16.Foundation.Persistence;

namespace Project16.CardGame.Tests
{
    public sealed class GamePersistenceTests
    {
        private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData);

        [Test]
        public void ExplicitFormatter_RoundTripsEveryCanonicalField_WithoutSharingCollections()
        {
            var original = FullState();
            var restored = Decode(Encode(original));
            AssertState(original, restored);
            restored.Players["p1"].Resources["gold"] = 999;
            restored.Cards["hand"].UpgradeIds.Clear();
            restored.Battle.Monsters["monster"].Counters.Clear();
            restored.Modifiers[0].RemainingOccurrences = 0;
            Assert.That(original.Players["p1"].Resources["gold"], Is.EqualTo(19));
            Assert.That(original.Cards["hand"].UpgradeIds, Is.EqualTo(new[] { "upgrade-a", "upgrade-b" }));
            Assert.That(original.Battle.Monsters["monster"].Counters["attacks"], Is.EqualTo(4));
            Assert.That(original.Modifiers[0].RemainingOccurrences, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitFormatter_CanonicalizesDictionaryOrder_AndLeavesGlobalOptionsUnchanged()
        {
            var global = MessagePackSerializer.DefaultOptions;
            var state = FullState();
            byte[] first = Encode(state);
            state.Players = state.Players.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
            state.Cards = state.Cards.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.That(Encode(state), Is.EqualTo(first));
            Assert.That(MessagePackSerializer.DefaultOptions, Is.SameAs(global));
        }

        [Test]
        public void Session_RestoresGameRevision_AndOnlySavesCommittedChanges()
        {
            var store = new MemoryStore();
            using (var writer = new LocalSaveSession(store))
            {
                var data = new GameData(writer.Load(GameData.SaveKey, GameData.CurrentSchema, FullState));
                writer.Track(data);
                Assert.That(writer.FlushDirty().SavedCount, Is.EqualTo(1));
                var proposed = data.CloneState();
                proposed.Counters["quest"] = 12;
                new MutationOwner().Commit(data, proposed);
                proposed.Counters["quest"] = 88;
                Assert.That(data.CaptureSnapshot().Counters["quest"], Is.EqualTo(12));
                Assert.That(writer.FlushDirty().SavedCount, Is.EqualTo(1));
                Assert.That(writer.FlushDirty().AttemptedCount, Is.Zero);
            }
            int beforeRead = store.Writes;
            using (var reader = new LocalSaveSession(store))
            {
                var loaded = reader.Load(GameData.SaveKey, GameData.CurrentSchema, () => new GameState());
                var restored = new GameData(loaded);
                reader.Track(restored);
                Assert.That(restored.Revision, Is.EqualTo(2));
                Assert.That(restored.CaptureSnapshot().Revision, Is.EqualTo(restored.Revision));
                Assert.That(restored.CaptureSnapshot().Counters["quest"], Is.EqualTo(12));
                Assert.That(restored.IsDirty, Is.False);
            }
            Assert.That(store.Writes, Is.EqualTo(beforeRead));
        }

        [Test]
        public void SavedSwitchWindow_ContinuesWithTheSameResponseAndModifierExpiry()
        {
            var state = ContinuationState();
            var expected = ContinueDeclinedSwitch(state.Clone());
            var resumed = ContinueDeclinedSwitch(Decode(Encode(state)));
            AssertState(expected, resumed);
            Assert.That(resumed.Players["p1"].Hp, Is.EqualTo(18));
            Assert.That(resumed.CurrentPlayerId, Is.EqualTo("p2"));
            Assert.That(resumed.Battle.Step, Is.EqualTo(BattleStep.PlayerAction));
            Assert.That(resumed.Battle.PendingResponsePlayerId, Is.Null);
            Assert.That(resumed.Modifiers, Is.Empty, "The source/owner anchor must survive and expire at p2's start.");
            Assert.That(resumed.RngState, Is.EqualTo(state.RngState));
            Assert.That(resumed.Counters["quest"], Is.EqualTo(7));
        }

        [Test]
        public void Formatter_RejectsNullState_AndNullRequiredCollections()
        {
            Assert.Throws<MessagePackSerializationException>(() => Decode(new byte[] { 0xc0 }));
            byte[] invalid = RewriteField(Encode(new GameState()), 13,
                (ref MessagePackWriter writer) => writer.WriteNil());
            Assert.Throws<MessagePackSerializationException>(() => Decode(invalid));
        }

        [Test]
        public void Formatter_RejectsUnknownOrMissingSlots_InsteadOfDroppingSavedState()
        {
            byte[] valid = Encode(new GameState());
            Assert.Throws<MessagePackSerializationException>(() => Decode(ChangeTopShape(valid, 1)));
            Assert.Throws<MessagePackSerializationException>(() => Decode(ChangeTopShape(valid, -1)));
        }

        [Test]
        public void Formatter_RejectsOversizedCollectionsAndStringsBeforeAllocatingThem()
        {
            byte[] valid = Encode(new GameState());
            byte[] count = RewriteField(valid, 13, (ref MessagePackWriter writer) =>
            {
                int entries = GameStateFormatter.MaximumCollectionEntries + 1;
                writer.WriteMapHeader(entries);
                // The SDK checks that a map header has enough remaining bytes before
                // returning its count. Supply complete entries so our allocation limit
                // is exercised, rather than the SDK's truncated-input check.
                for (int i = 0; i < entries; i++)
                {
                    writer.Write(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    writer.WriteNil();
                }
            });
            var countError = Assert.Throws<MessagePackSerializationException>(() => Decode(count));
            Assert.That(countError.ToString(), Does.Contain("collection limit"));
            byte[] text = RewriteField(valid, 4, (ref MessagePackWriter writer) =>
                writer.Write(new string('x', GameStateFormatter.MaximumStringBytes + 1)));
            var textError = Assert.Throws<MessagePackSerializationException>(() => Decode(text));
            Assert.That(textError.ToString(), Does.Contain("string limit"));
        }

        [Test]
        public void Formatter_RejectsDuplicateMapKeys_AndInvalidCardZoneOwnership()
        {
            byte[] invalid = RewriteField(Encode(new GameState()), 18, (ref MessagePackWriter writer) =>
            {
                writer.WriteMapHeader(2);
                writer.Write("same"); writer.Write(1);
                writer.Write("same"); writer.Write(2);
            });
            var duplicate = Assert.Throws<MessagePackSerializationException>(() => Decode(invalid));
            Assert.That(duplicate.ToString(), Does.Contain("duplicate map key"));
            var invalidState = FullState();
            invalidState.Players["p1"].Discard.Add("hand");
            Assert.Throws<MessagePackSerializationException>(() => Encode(invalidState));
        }

        [Test]
        public void ValidEnvelopeWithCorruptGamePayload_DoesNotCreateDefaultsOrOverwriteData()
        {
            var store = new MemoryStore();
            byte[] malformed = RewriteField(Encode(new GameState()), 13,
                (ref MessagePackWriter writer) => writer.WriteNil());
            using (var seed = new LocalSaveSession(store))
            {
                var loaded = seed.Load(GameData.SaveKey, 1, () => new RawPayload(malformed));
                seed.Track(new RawUnit(loaded));
            }
            byte[] before = (byte[])store.Primary.Clone();
            bool defaultCalled = false;
            using (var reader = new LocalSaveSession(store))
            {
                var error = Assert.Throws<LocalSaveLoadException>(() => reader.Load(GameData.SaveKey, 1, () =>
                {
                    defaultCalled = true;
                    return new GameState();
                }));
                Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.CorruptData));
            }
            Assert.That(defaultCalled, Is.False);
            Assert.That(store.Primary, Is.EqualTo(before));
            Assert.That(store.Writes, Is.EqualTo(1));
        }

        [Test]
        public void FutureGameSchema_DoesNotFallBackToOlderBackupOrOverwriteIt()
        {
            var store = new MemoryStore();
            using (var original = new LocalSaveSession(store))
            {
                var loaded = original.Load(GameData.SaveKey, 1, () => new GameState());
                original.Track(new SchemaUnit(1, loaded));
            }
            using (var future = new LocalSaveSession(store))
            {
                var loaded = future.Load(GameData.SaveKey, 2, () => new GameState(),
                    (version, bytes, options) => MessagePackSerializer.Deserialize<GameState>(bytes, options));
                future.Track(new SchemaUnit(2, loaded));
            }
            byte[] before = (byte[])store.Primary.Clone();
            byte[] backup = (byte[])store.Backup.Clone();
            using (var reader = new LocalSaveSession(store))
            {
                var error = Assert.Throws<LocalSaveLoadException>(() =>
                    reader.Load(GameData.SaveKey, 1, () => new GameState()));
                Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.UnsupportedVersion));
            }
            Assert.That(store.Primary, Is.EqualTo(before));
            Assert.That(store.Backup, Is.EqualTo(backup));
            Assert.That(store.Writes, Is.EqualTo(2));
        }

        [Test]
        public void GameReadFailure_DoesNotRunDefaultsOrWriteAReplacement()
        {
            var store = new MemoryStore { FailRead = true, Primary = new byte[] { 1, 2, 3 } };
            bool defaultCalled = false;
            using (var session = new LocalSaveSession(store))
            {
                var error = Assert.Throws<LocalSaveLoadException>(() => session.Load(GameData.SaveKey, 1, () =>
                {
                    defaultCalled = true;
                    return new GameState();
                }));
                Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.ReadFailed));
            }
            Assert.That(defaultCalled, Is.False);
            Assert.That(store.Writes, Is.Zero);
            Assert.That(store.Primary, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void CorruptGamePrimary_RecoversAndRepairsWithoutDestroyingTheGoodBackup()
        {
            var store = new MemoryStore();
            using (var writer = new LocalSaveSession(store))
            {
                var data = new GameData(writer.Load(GameData.SaveKey, 1, FullState));
                writer.Track(data);
                writer.FlushDirty();
                var next = data.CloneState(); next.Round++;
                new MutationOwner().Commit(data, next);
            }
            byte[] backup = (byte[])store.Backup.Clone();
            store.Primary = new byte[] { 0xc1 };
            using (var reader = new LocalSaveSession(store))
            {
                var loaded = reader.Load(GameData.SaveKey, 1, () => new GameState());
                Assert.That(loaded.RecoveredFromBackup, Is.True);
                Assert.That(loaded.Snapshot.Round, Is.EqualTo(3));
                reader.Track(new GameData(loaded));
                Assert.That(reader.FlushDirty().Succeeded, Is.True);
            }
            Assert.That(store.Backup, Is.EqualTo(backup));
        }

        private static byte[] Encode(GameState state) => MessagePackSerializer.Serialize(state, Options);
        private static GameState Decode(byte[] bytes) => MessagePackSerializer.Deserialize<GameState>(bytes, Options);

        private static GameState FullState()
        {
            var state = new GameState
            {
                Revision = 9, Round = 3, TurnSerial = 17, PhaseSerial = 8, PhaseId = "combat",
                CurrentPlayerId = "p1", LeaderPlayerId = "p2", IsSolo = false,
                Defeated = false, Victorious = true, OutcomeReason = "cleared", RngState = ulong.MaxValue - 17,
                NextInstanceId = 901
            };
            var first = new PlayerState
            {
                Id = "p1", ActorSpecId = "actor", Team = "players", Hp = 17, MaxHp = 25, BaseAttack = 4,
                TurnCount = 7, CardActionsUsed = 2, ItemActionsUsed = 1, Escaped = true
            };
            first.CompanionIds.Add("companion"); first.Tags.Add("tag-a");
            first.Resources.Add("gold", 19); first.Resources.Add("energy", 3);
            state.Players.Add(first.Id, first); state.PlayerOrder.Add(first.Id);
            state.Players.Add("p2", new PlayerState { Id = "p2", ActorSpecId = "actor", Team = "players", Hp = 12, MaxHp = 20 });
            state.PlayerOrder.Add("p2");
            foreach (var pair in new[] { ("deck", CardZone.Deck), ("hand", CardZone.Hand),
                ("discard", CardZone.Discard), ("removed", CardZone.Removed), ("equipped", CardZone.Equipped) })
            {
                var card = new CardInstanceState
                {
                    Id = pair.Item1, SpecId = "skill", OwnerId = "p1", CompanionId = "companion",
                    EquippedTo = pair.Item2 == CardZone.Equipped ? "hand" : null, Zone = pair.Item2, UsesThisTurn = 2
                };
                card.UpgradeIds.AddRange(new[] { "upgrade-a", "upgrade-b" });
                card.AttachedEffectGroupIds.Add("attached-effect"); card.AttachedModifierIds.Add("attached-modifier");
                card.AbilityUseCounts.Add("ability-a", 6);
                state.Cards.Add(card.Id, card);
                switch (card.Zone)
                {
                    case CardZone.Deck: first.Deck.Add(card.Id); break;
                    case CardZone.Hand: first.Hand.Add(card.Id); break;
                    case CardZone.Discard: first.Discard.Add(card.Id); break;
                    case CardZone.Removed: first.Removed.Add(card.Id); break;
                    case CardZone.Equipped: first.Equipped.Add(card.Id); break;
                }
            }
            state.Counters.Add("quest", 7);
            state.Modifiers.Add(new ModifierState
            {
                Id = "mod-1", SpecId = "guard", OwnerId = "p1", TargetId = "p2", SourceId = "hand", AnchorPlayerId = "p1",
                Stacks = 3, RemainingOccurrences = 2, TriggerOccurrences = 4, AppliedRound = 2,
                AppliedTurnSerial = 15, AppliedPhaseSerial = 6, LastTriggerTurnSerial = 16, LastTriggerPhaseSerial = 7
            });
            state.Battle = new BattleState
            {
                EncounterId = "encounter", Step = BattleStep.SwitchWindow, ActivePlayerIndex = 1,
                ExtraAttacks = 2, ActiveCardId = "hand", LastActingPlayerId = "p2", PendingResponsePlayerId = "p1",
                PendingEscape = true, Completed = true
            };
            state.Battle.ParticipantIds.AddRange(state.PlayerOrder);
            var monster = new MonsterState { Id = "monster", SpecId = "monster-spec", Hp = 9, MaxHp = 31 };
            monster.Tags.Add("boss"); monster.Counters.Add("attacks", 4);
            state.Battle.Monsters.Add(monster.Id, monster); state.Battle.MonsterOrder.Add(monster.Id);
            state.Battle.LastHitPlayerByMonster.Add(monster.Id, "p2");
            state.Validate();
            return state;
        }

        private static GameState ContinuationState()
        {
            var state = new GameState
            {
                PhaseId = "combat", CurrentPlayerId = "p1", LeaderPlayerId = "p1", TurnSerial = 4,
                PhaseSerial = 2, RngState = 123456789, NextInstanceId = 99
            };
            foreach (string id in new[] { "p1", "p2" })
            {
                state.Players.Add(id, new PlayerState { Id = id, ActorSpecId = "actor", Team = "players", Hp = 20, MaxHp = 20 });
                state.PlayerOrder.Add(id);
            }
            state.Counters.Add("quest", 7);
            state.Battle.EncounterId = "encounter";
            state.Battle.Step = BattleStep.SwitchWindow;
            state.Battle.ParticipantIds.AddRange(state.PlayerOrder);
            state.Battle.PendingResponsePlayerId = "p1";
            state.Battle.LastActingPlayerId = "p1";
            state.Battle.Monsters.Add("monster", new MonsterState { Id = "monster", SpecId = "monster-spec", Hp = 20, MaxHp = 20 });
            state.Battle.MonsterOrder.Add("monster");
            state.Modifiers.Add(new ModifierState
            {
                Id = "mod", SpecId = "guard", OwnerId = "p2", TargetId = "p2", SourceId = "p1", AnchorPlayerId = "p2",
                RemainingOccurrences = 1, AppliedTurnSerial = 3, AppliedPhaseSerial = 1
            });
            return state;
        }

        private static GameState ContinueDeclinedSwitch(GameState state)
        {
            var specs = new GameSpecCatalog(new GameRulesSpec("combat"),
                phases: new[] { new PhaseSpec("combat", PhaseKind.Combat) },
                actors: new[] { new ActorSpec("actor", 20) },
                durations: new[] { new DurationSpec("until-owner", DurationKind.OwnerTurnStart) },
                modifiers: new[] { new ModifierSpec("guard", ModifierKind.Stat, "until-owner", amount: 3) },
                monsters: new[] { new MonsterSpec("monster-spec", 20, attack: 2) },
                encounters: new[] { new EncounterSpec("encounter", new[] { "monster-spec" }) });
            var builder = new ScopeBuilder();
            builder.Bind<IGameSpecs>().FromInstance(specs);
            builder.Bind<GameData>().FromFactory(() => new GameData(state)).AsScoped();
            builder.Bind<IGameRulesDomain>().To<GameRulesDomain>().AsScoped();
            using (var scope = builder.Build())
            {
                var result = scope.Resolve<IGameRulesDomain>().Execute(new GameCommand(GameCommandKind.DeclineSwitch, "p2"));
                Assert.That(result.Code, Is.EqualTo(CommandResultCode.Success), result.Message);
                return scope.Resolve<GameData>().CaptureSnapshot();
            }
        }

        private static void AssertState(GameState expected, GameState actual)
        {
            Assert.That(new object[] { actual.Revision, actual.Round, actual.TurnSerial, actual.PhaseSerial, actual.PhaseId,
                actual.CurrentPlayerId, actual.LeaderPlayerId, actual.IsSolo, actual.Defeated, actual.Victorious,
                actual.OutcomeReason, actual.RngState, actual.NextInstanceId }, Is.EqualTo(new object[] {
                expected.Revision, expected.Round, expected.TurnSerial, expected.PhaseSerial, expected.PhaseId,
                expected.CurrentPlayerId, expected.LeaderPlayerId, expected.IsSolo, expected.Defeated, expected.Victorious,
                expected.OutcomeReason, expected.RngState, expected.NextInstanceId }));
            Assert.That(actual.PlayerOrder, Is.EqualTo(expected.PlayerOrder));
            Assert.That(actual.Counters, Is.EquivalentTo(expected.Counters));
            Assert.That(actual.Players.Keys, Is.EquivalentTo(expected.Players.Keys));
            foreach (var pair in expected.Players)
            {
                var e = pair.Value; var a = actual.Players[pair.Key];
                Assert.That(new object[] { a.Id, a.ActorSpecId, a.Team, a.Hp, a.MaxHp, a.BaseAttack, a.TurnCount,
                    a.CardActionsUsed, a.ItemActionsUsed, a.Escaped }, Is.EqualTo(new object[] {
                    e.Id, e.ActorSpecId, e.Team, e.Hp, e.MaxHp, e.BaseAttack, e.TurnCount, e.CardActionsUsed, e.ItemActionsUsed, e.Escaped }));
                Assert.That(a.CompanionIds, Is.EqualTo(e.CompanionIds)); Assert.That(a.Tags, Is.EqualTo(e.Tags));
                Assert.That(a.Deck, Is.EqualTo(e.Deck)); Assert.That(a.Hand, Is.EqualTo(e.Hand));
                Assert.That(a.Discard, Is.EqualTo(e.Discard)); Assert.That(a.Removed, Is.EqualTo(e.Removed));
                Assert.That(a.Equipped, Is.EqualTo(e.Equipped)); Assert.That(a.Resources, Is.EquivalentTo(e.Resources));
            }
            Assert.That(actual.Cards.Keys, Is.EquivalentTo(expected.Cards.Keys));
            foreach (var pair in expected.Cards)
            {
                var e = pair.Value; var a = actual.Cards[pair.Key];
                Assert.That(new object[] { a.Id, a.SpecId, a.OwnerId, a.CompanionId, a.EquippedTo, a.Zone, a.UsesThisTurn },
                    Is.EqualTo(new object[] { e.Id, e.SpecId, e.OwnerId, e.CompanionId, e.EquippedTo, e.Zone, e.UsesThisTurn }));
                Assert.That(a.UpgradeIds, Is.EqualTo(e.UpgradeIds)); Assert.That(a.AttachedEffectGroupIds, Is.EqualTo(e.AttachedEffectGroupIds));
                Assert.That(a.AttachedModifierIds, Is.EqualTo(e.AttachedModifierIds)); Assert.That(a.AbilityUseCounts, Is.EquivalentTo(e.AbilityUseCounts));
            }
            Assert.That(actual.Modifiers.Count, Is.EqualTo(expected.Modifiers.Count));
            for (int i = 0; i < expected.Modifiers.Count; i++)
            {
                var e = expected.Modifiers[i]; var a = actual.Modifiers[i];
                Assert.That(new object[] { a.Id, a.SpecId, a.OwnerId, a.TargetId, a.SourceId, a.AnchorPlayerId, a.Stacks,
                    a.RemainingOccurrences, a.TriggerOccurrences, a.AppliedRound, a.AppliedTurnSerial, a.AppliedPhaseSerial,
                    a.LastTriggerTurnSerial, a.LastTriggerPhaseSerial }, Is.EqualTo(new object[] {
                    e.Id, e.SpecId, e.OwnerId, e.TargetId, e.SourceId, e.AnchorPlayerId, e.Stacks, e.RemainingOccurrences,
                    e.TriggerOccurrences, e.AppliedRound, e.AppliedTurnSerial, e.AppliedPhaseSerial, e.LastTriggerTurnSerial, e.LastTriggerPhaseSerial }));
            }
            var eb = expected.Battle; var ab = actual.Battle;
            Assert.That(new object[] { ab.EncounterId, ab.Step, ab.ActivePlayerIndex, ab.ExtraAttacks, ab.ActiveCardId,
                ab.LastActingPlayerId, ab.PendingResponsePlayerId, ab.PendingEscape, ab.Completed }, Is.EqualTo(new object[] {
                eb.EncounterId, eb.Step, eb.ActivePlayerIndex, eb.ExtraAttacks, eb.ActiveCardId, eb.LastActingPlayerId,
                eb.PendingResponsePlayerId, eb.PendingEscape, eb.Completed }));
            Assert.That(ab.ParticipantIds, Is.EqualTo(eb.ParticipantIds)); Assert.That(ab.MonsterOrder, Is.EqualTo(eb.MonsterOrder));
            Assert.That(ab.LastHitPlayerByMonster, Is.EquivalentTo(eb.LastHitPlayerByMonster));
            Assert.That(ab.Monsters.Keys, Is.EquivalentTo(eb.Monsters.Keys));
            foreach (var pair in eb.Monsters)
            {
                var e = pair.Value; var a = ab.Monsters[pair.Key];
                Assert.That(new object[] { a.Id, a.SpecId, a.Hp, a.MaxHp }, Is.EqualTo(new object[] { e.Id, e.SpecId, e.Hp, e.MaxHp }));
                Assert.That(a.Tags, Is.EqualTo(e.Tags)); Assert.That(a.Counters, Is.EquivalentTo(e.Counters));
            }
        }

        private delegate void WritePayload(ref MessagePackWriter writer);

        private static byte[] RewriteField(byte[] original, int index, WritePayload replacement)
        {
            var reader = new MessagePackReader(new ReadOnlyMemory<byte>(original));
            var buffer = new ArrayBufferWriter<byte>(); var writer = new MessagePackWriter(buffer);
            int count = reader.ReadArrayHeader(); writer.WriteArrayHeader(count);
            for (int i = 0; i < count; i++)
                if (i == index) { reader.Skip(); replacement(ref writer); }
                else writer.WriteRaw(reader.ReadRaw());
            writer.Flush(); return buffer.WrittenSpan.ToArray();
        }

        private static byte[] ChangeTopShape(byte[] original, int difference)
        {
            var reader = new MessagePackReader(new ReadOnlyMemory<byte>(original));
            var buffer = new ArrayBufferWriter<byte>(); var writer = new MessagePackWriter(buffer);
            int count = reader.ReadArrayHeader(); writer.WriteArrayHeader(count + difference);
            for (int i = 0; i < Math.Min(count, count + difference); i++) writer.WriteRaw(reader.ReadRaw());
            if (difference > 0) writer.WriteNil();
            writer.Flush(); return buffer.WrittenSpan.ToArray();
        }

        private sealed class MutationOwner : IDomainServiceLayer<GameData>
        {
            public void Commit(GameData data, GameState state) => data.Commit(state);
        }

        private sealed class SchemaUnit : UserDataUnit<GameState>
        {
            private readonly GameState _state;
            public SchemaUnit(int schema, SaveLoadResult<GameState> loaded) : base(GameData.SaveKey, schema, loaded) => _state = loaded.Snapshot;
            public override GameState CaptureSnapshot() => _state.Clone();
        }

        [MessagePackFormatter(typeof(RawPayloadFormatter))]
        public sealed class RawPayload
        {
            public readonly byte[] Bytes;
            public RawPayload(byte[] bytes) => Bytes = bytes;
        }

        public sealed class RawPayloadFormatter : IMessagePackFormatter<RawPayload>
        {
            public void Serialize(ref MessagePackWriter writer, RawPayload value, MessagePackSerializerOptions options) => writer.WriteRaw(value.Bytes);
            public RawPayload Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => new RawPayload(reader.ReadRaw().ToArray());
        }

        private sealed class RawUnit : UserDataUnit<RawPayload>
        {
            private readonly RawPayload _value;
            public RawUnit(SaveLoadResult<RawPayload> loaded) : base(GameData.SaveKey, 1, loaded) => _value = loaded.Snapshot;
            public override RawPayload CaptureSnapshot() => _value;
        }

        private sealed class MemoryStore : ILocalSaveStore
        {
            public byte[] Primary;
            public byte[] Backup;
            public int Writes;
            public bool FailRead;
            public byte[] ReadPrimary(string key)
            {
                if (FailRead) throw new IOException("Read denied.");
                return Primary == null ? null : (byte[])Primary.Clone();
            }
            public byte[] ReadBackup(string key) => Backup == null ? null : (byte[])Backup.Clone();
            public void Write(string key, byte[] envelope, bool preserveBackup)
            {
                if (!preserveBackup && Primary != null) Backup = (byte[])Primary.Clone();
                Primary = (byte[])envelope.Clone(); Writes++;
            }
        }
    }
}
