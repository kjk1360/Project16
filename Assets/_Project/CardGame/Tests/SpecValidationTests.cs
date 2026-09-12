using System;
using System.Collections.Generic;
using Kylin.DI.Layered;
using NUnit.Framework;
using Project16.Foundation.Specs;

namespace Project16.CardGame.Tests
{
    public sealed class SpecValidationTests
    {
        [Test]
        public void GenericSkillsAreAllowed_ButUnknownCompanionReferencesAreNot()
        {
            Assert.DoesNotThrow(() => Catalog(cards: new[] { new CardSpec("generic", CardKind.Skill) }));
            Assert.Throws<SpecValidationException>(() => Catalog(cards: new[] { new CardSpec("owned", CardKind.Skill, companionId: "absent") }));
        }

        [Test]
        public void DuplicateIdentifiersFailBeforeTheCatalogCanBeUsed()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(cards: new[]
            { new CardSpec("same", CardKind.Skill), new CardSpec("same", CardKind.Item) }));
        }

        [Test]
        public void MissingEffectReferencesAreRejected()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[]
            { new EffectSpec("effect", "missing", 0, EffectOperation.Heal, "self", 1) }));
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[]
            { new EffectSpec("effect", "group", 0, EffectOperation.Heal, "missing", 1) }));
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[]
            { new EffectSpec("effect", "group", 0, EffectOperation.ApplyModifier, "self", referenceId: "missing") }));
        }

        [Test]
        public void UndefinedEnumValuesAreRejectedAcrossTheSpecFamilies()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(cards: new[] { new CardSpec("bad", (CardKind)999) }));
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[] { new EffectSpec("bad", "group", 0, (EffectOperation)999, "self") }));
            Assert.Throws<SpecValidationException>(() => Catalog(selectors: new[] { new TargetSelectorSpec("self", new[] { (TargetKind)999 }) }));
            Assert.Throws<SpecValidationException>(() => Catalog(conditions: new[] { new ConditionSpec("bad", (ConditionKind)999) }));
            Assert.Throws<SpecValidationException>(() => Catalog(durations: new[] { new DurationSpec("duration", (DurationKind)999) }));
            Assert.Throws<SpecValidationException>(() => Catalog(modifiers: new[] { new ModifierSpec("bad", (ModifierKind)999, "duration") }));
            Assert.Throws<SpecValidationException>(() => Catalog(modifiers: new[] { new ModifierSpec("bad", ModifierKind.Stat, "duration", stackPolicy: (StackPolicy)999) }));
        }

        [Test]
        public void EffectOrderIsExplicitAndUnique()
        {
            var effects = new[]
            {
                new EffectSpec("later", "group", 20, EffectOperation.Heal, "self", 2),
                new EffectSpec("earlier", "group", 10, EffectOperation.Heal, "self", 1)
            };
            var catalog = Catalog(effects: effects);
            Assert.That(catalog.Effects("group")[0].Key, Is.EqualTo("earlier"));
            Assert.That(catalog.Effects("group")[1].Key, Is.EqualTo("later"));
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[]
            {
                new EffectSpec("first", "group", 1, EffectOperation.Heal, "self"),
                new EffectSpec("second", "group", 1, EffectOperation.Heal, "self")
            }));
        }

        [Test]
        public void SynchronousEffectRecursionIsRejected()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(
                groups: new[] { new EffectGroupSpec("a"), new EffectGroupSpec("b") },
                effects: new[]
                {
                    new EffectSpec("a-b", "a", 0, EffectOperation.RunGroup, "self", referenceId: "b"),
                    new EffectSpec("b-a", "b", 0, EffectOperation.RunGroup, "self", referenceId: "a")
                }));
        }

        [Test]
        public void LogicalConditionCyclesAndWrongNotArityAreRejected()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(conditions: new[]
            {
                new ConditionSpec("a", ConditionKind.Not, children: new[] { "b" }),
                new ConditionSpec("b", ConditionKind.All, children: new[] { "a" })
            }));
            Assert.Throws<SpecValidationException>(() => Catalog(conditions: new[]
            {
                new ConditionSpec("a", ConditionKind.Always),
                new ConditionSpec("b", ConditionKind.Not, children: new[] { "a", "a" })
            }));
        }

        [Test]
        public void AutomaticEncounterCyclesAreRejected_WhilePhaseLoopsAreAllowed()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(
                monsters: new[] { new MonsterSpec("monster", 1) },
                encounters: new[]
                {
                    new EncounterSpec("a", new[] { "monster" }, nextEncounterId: "b"),
                    new EncounterSpec("b", new[] { "monster" }, nextEncounterId: "a")
                }));
            Assert.DoesNotThrow(() => Catalog(phases: new[]
            {
                new PhaseSpec("start", PhaseKind.Start, "end"),
                new PhaseSpec("end", PhaseKind.End, "start")
            }));
        }

        [Test]
        public void FiniteDurationsRequirePositiveOccurrencesAndAppropriatePhaseFilter()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(durations: new[] { new DurationSpec("duration", DurationKind.OwnerTurnEnd, 0) }));
            Assert.Throws<SpecValidationException>(() => Catalog(durations: new[] { new DurationSpec("duration", DurationKind.GlobalPhaseEnd, 1, "absent") }));
            Assert.Throws<SpecValidationException>(() => Catalog(durations: new[] { new DurationSpec("duration", DurationKind.OwnerTurnStart, 2, "start") }));
            Assert.DoesNotThrow(() => Catalog(durations: new[] { new DurationSpec("duration", DurationKind.OwnerTurnEnd, 3, anchor: DurationAnchor.SourceOwner) }));
        }

        [Test]
        public void ModifierStackAndPeriodicDefinitionsMustBeComplete()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(modifiers: new[] { new ModifierSpec("bad", ModifierKind.Stat, "duration", maxStacks: 0) }));
            Assert.Throws<SpecValidationException>(() => Catalog(modifiers: new[] { new ModifierSpec("bad", ModifierKind.PeriodicTrigger, "duration") }));
            Assert.Throws<SpecValidationException>(() => Catalog(modifiers: new[] { new ModifierSpec("bad", ModifierKind.PeriodicTrigger, "duration", effectGroupId: "group", trigger: GameEventKind.TurnStart, everyOccurrences: 0) }));
            Assert.Throws<SpecValidationException>(() => Catalog(modifiers: new[] { new ModifierSpec("bad", ModifierKind.Stat, "duration", effectGroupId: "group", trigger: GameEventKind.TurnStart) }));
        }

        [Test]
        public void CleanseRequiresATag_AndCanReferenceASetOfModifierKinds()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[] { new EffectSpec("cleanse", "group", 0, EffectOperation.RemoveModifiersByTag, "self") }));
            Assert.DoesNotThrow(() => Catalog(effects: new[] { new EffectSpec("cleanse", "group", 0, EffectOperation.RemoveModifiersByTag, "self", referenceId: "paralysis") }));
        }

        [Test]
        public void CostAndValueDefinitionsRejectMissingResourceKeysAndZeroDivision()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(cards: new[] { new CardSpec("bad", CardKind.Skill, cost: 2) }));
            Assert.Throws<SpecValidationException>(() => Catalog(values: new[] { new ValueSpec("bad", ValueKind.Constant, 2, divisor: 0) }));
            Assert.Throws<SpecValidationException>(() => Catalog(values: new[] { new ValueSpec("bad", ValueKind.SourceResource) }));
            Assert.Throws<SpecValidationException>(() => Catalog(cards: new[] { new CardSpec("recursive", CardKind.Skill, attackValueId: "attack") },
                values: new[] { new ValueSpec("attack", ValueKind.SourceAttack) }));
        }

        [Test]
        public void PrimitiveEffectsRejectImpossibleTargetKindsAndStaticAmounts()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[] { new EffectSpec("bad", "group", 0, EffectOperation.Heal, "self", -1) }));
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[] { new EffectSpec("bad", "group", 0, EffectOperation.MoveCard, "self") }));
            Assert.Throws<SpecValidationException>(() => Catalog(effects: new[] { new EffectSpec("bad", "group", 0, EffectOperation.Damage, "self", 1) },
                selectors: new[] { new TargetSelectorSpec("self", new[] { TargetKind.SelectedCard }) }));
            Assert.Throws<SpecValidationException>(() => Catalog(cards: new[] { new CardSpec("bad", CardKind.Skill, allowedPhases: Array.Empty<PhaseKind>()) }));
        }

        [Test]
        public void CollectionsAreCopiedAndReadOnly_AndRoleMultiplicityIsPreserved()
        {
            var tags = new List<string> { "basic" };
            var roles = new List<TargetKind> { TargetKind.CurrentPlayer, TargetKind.NextPlayer };
            var card = new CardSpec("skill", CardKind.Skill, tags: tags);
            var selector = new TargetSelectorSpec("self", roles);
            var catalog = Catalog(cards: new[] { card }, selectors: new[] { selector });
            tags[0] = "changed"; roles.Clear();
            Assert.That(catalog.Card("skill").Tags[0], Is.EqualTo("basic"));
            Assert.That(catalog.Selector("self").Targets.Count, Is.EqualTo(2));
            Assert.That(catalog.Selector("self").DeduplicateTargets, Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)catalog.Card("skill").Tags).Add("outside"));
        }

        [Test]
        public void EmptySelectorsAndDuplicateTagsAreRejected()
        {
            Assert.Throws<SpecValidationException>(() => Catalog(selectors: new[] { new TargetSelectorSpec("self", Array.Empty<TargetKind>()) }));
            Assert.Throws<SpecValidationException>(() => Catalog(cards: new[] { new CardSpec("bad", CardKind.Skill, tags: new[] { "same", "same" }) }));
        }

        [Test]
        public void SnapshotAndWorkingCopiesCannotMutateTheCommittedData()
        {
            GameState source = State();
            using var data = new GameData(source);
            source.Players["p"].Hp = 1;
            Assert.That(data.Snapshot.Players["p"].Hp, Is.EqualTo(10));
            GameSnapshot snapshot = data.Snapshot;
            GameState working = data.CloneState();
            working.Players["p"].Hp = 5;
            working.Cards["card"].UpgradeIds.Add("upgrade");
            snapshot.ToState().Players["p"].Hand.Clear();
            Assert.That(data.Snapshot.Players["p"].Hp, Is.EqualTo(10));
            Assert.That(snapshot.Players["p"].Hand.Count, Is.EqualTo(1));
            Assert.That(data.Snapshot.Cards["card"].UpgradeIds, Is.Empty);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)snapshot.Players["p"].Hand).Clear());
        }

        [Test]
        public void CommitCopiesTheTransaction_IncrementsRevision_AndDoesNotMutateOlderSnapshots()
        {
            using var data = new GameData(State());
            long revision = data.Revision;
            GameSnapshot old = data.Snapshot;
            GameState working = data.CloneState();
            working.Players["p"].Hp = 4;
            TestGameOwner.Commit(data, working);
            working.Players["p"].Hp = 1;
            Assert.That(data.Revision, Is.EqualTo(revision + 1));
            Assert.That(data.CloneState().Revision, Is.EqualTo(data.Revision));
            Assert.That(data.Snapshot.Players["p"].Hp, Is.EqualTo(4));
            Assert.That(old.Players["p"].Hp, Is.EqualTo(10));
            Assert.That(data.IsDirty, Is.True);
        }

        [Test]
        public void InvalidTransactionDoesNotChangeStateOrRevision()
        {
            using var data = new GameData(State());
            long revision = data.Revision;
            GameState invalid = data.CloneState();
            invalid.Players["p"].Hand.Add("card");
            Assert.Throws<InvalidOperationException>(() => TestGameOwner.Commit(data, invalid));
            Assert.That(data.Revision, Is.EqualTo(revision));
            Assert.That(data.Snapshot.Players["p"].Hand.Count, Is.EqualTo(1));
            invalid = data.CloneState(); invalid.Players["p"].Hp = -1;
            Assert.Throws<InvalidOperationException>(() => TestGameOwner.Commit(data, invalid));
            Assert.That(data.Snapshot.Players["p"].Hp, Is.EqualTo(10));
        }

        [Test]
        public void NegativeRestHealingIsRejected()
        {
            var phases = new[] { new PhaseSpec("start", PhaseKind.Start) };
            Assert.Throws<SpecValidationException>(() => new GameSpecCatalog(new GameRulesSpec("start", restHeal: -1), phases));
            Assert.Throws<SpecValidationException>(() => new GameSpecCatalog(new GameRulesSpec("start", restWithExileHeal: -1), phases));
        }

        [Test]
        public void IntrinsicUpgradeModifiersRejectUnsupportedKinds()
        {
            var upgrade = new[] { new UpgradeSpec("upgrade", modifierId: "modifier") };
            Assert.Throws<SpecValidationException>(() => Catalog(upgrades: upgrade,
                modifiers: new[] { new ModifierSpec("modifier", ModifierKind.Stat, "duration", stat: StatKind.DamageTaken) }));
            Assert.Throws<SpecValidationException>(() => Catalog(upgrades: upgrade,
                modifiers: new[] { new ModifierSpec("modifier", ModifierKind.PeriodicTrigger, "duration", effectGroupId: "group", trigger: GameEventKind.TurnStart) }));
            Assert.DoesNotThrow(() => Catalog(upgrades: upgrade,
                modifiers: new[] { new ModifierSpec("modifier", ModifierKind.Stat, "duration", stat: StatKind.Attack) }));
            Assert.DoesNotThrow(() => Catalog(upgrades: upgrade,
                modifiers: new[] { new ModifierSpec("modifier", ModifierKind.PreventAction, "duration") }));
        }

        [Test]
        public void LoadedSpecReferencesAreCheckedBeforeTheRunIsUsed()
        {
            var catalog = Catalog(actors: new[] { new ActorSpec("actor", 10) }, cards: new[] { new CardSpec("skill", CardKind.Skill) });
            Assert.DoesNotThrow(() => GameStateSpecValidation.Validate(new GameState(), catalog));
            Assert.DoesNotThrow(() => GameStateSpecValidation.Validate(State(), catalog));
            Action<GameState>[] invalid =
            {
                state => state.PhaseId = "missing",
                state => state.Players["p"].ActorSpecId = "missing",
                state => state.Players["p"].CompanionIds.Add("missing"),
                state => state.Cards["card"].SpecId = "missing",
                state => state.Cards["card"].UpgradeIds.Add("missing"),
                state => state.Cards["card"].AttachedEffectGroupIds.Add("missing"),
                state => state.Cards["card"].AttachedModifierIds.Add("missing"),
                state => state.Modifiers.Add(new ModifierState { Id = "mod", SpecId = "missing", TargetId = "p" }),
                state => state.Battle.EncounterId = "missing",
                state => state.Cards["card"].EquippedTo = "missing"
            };
            foreach (var mutate in invalid)
            {
                GameState state = State(); mutate(state);
                Assert.Throws<SpecValidationException>(() => GameStateSpecValidation.Validate(state, catalog));
            }
        }

        private static GameSpecCatalog Catalog(IEnumerable<CardSpec> cards = null, IEnumerable<EffectSpec> effects = null,
            IEnumerable<EffectGroupSpec> groups = null, IEnumerable<TargetSelectorSpec> selectors = null,
            IEnumerable<ConditionSpec> conditions = null, IEnumerable<DurationSpec> durations = null,
            IEnumerable<ModifierSpec> modifiers = null, IEnumerable<ValueSpec> values = null,
            IEnumerable<MonsterSpec> monsters = null, IEnumerable<EncounterSpec> encounters = null,
            IEnumerable<PhaseSpec> phases = null, IEnumerable<UpgradeSpec> upgrades = null, IEnumerable<ActorSpec> actors = null)
        {
            return new GameSpecCatalog(new GameRulesSpec("start"),
                phases: phases ?? new[] { new PhaseSpec("start", PhaseKind.Start) }, cards: cards,
                groups: groups ?? new[] { new EffectGroupSpec("group") }, effects: effects,
                selectors: selectors ?? new[] { new TargetSelectorSpec("self", new[] { TargetKind.Self }) },
                conditions: conditions, durations: durations ?? new[] { new DurationSpec("duration", DurationKind.CurrentTurn) },
                modifiers: modifiers, values: values, monsters: monsters, encounters: encounters, upgrades: upgrades, actors: actors);
        }

        private static GameState State()
        {
            var state = new GameState { CurrentPlayerId = "p", LeaderPlayerId = "p", PhaseId = "start" };
            var player = new PlayerState { Id = "p", ActorSpecId = "actor", Team = "players", Hp = 10, MaxHp = 10 };
            player.Hand.Add("card"); state.Players.Add("p", player); state.PlayerOrder.Add("p");
            state.Cards.Add("card", new CardInstanceState { Id = "card", SpecId = "skill", OwnerId = "p", Zone = CardZone.Hand });
            return state;
        }

        private sealed class TestGameOwner : IDomainServiceLayer<GameData>
        {
            public static void Commit(GameData data, GameState transaction) => data.Commit(transaction);
        }
    }
}
