using System;
using System.Collections.Generic;
using System.Linq;
using Kylin.DI;
using NUnit.Framework;
using Project16.CardGame.Domain;

namespace Project16.CardGame.Tests
{
    public sealed class DomainTests
    {
        [Test]
        public void MissingOrInvalidChoice_RollsBackCardsBudgetsRandomAndRevision()
        {
            using var fixture = new Fixture(State(monsters: 2), Catalog(monsterCount: 2));
            var before = fixture.Data.CloneState();
            var missing = fixture.Execute(GameCommandKind.PlayCards, "p1", "c1");
            Assert.That(missing.Code, Is.EqualTo(CommandResultCode.NeedsChoice));
            Assert.That(missing.Choice.Key, Is.EqualTo("$attack"));
            Assert.That(missing.Choice.Options, Is.EquivalentTo(new[] { "m1", "m2" }));
            var after = fixture.Data.CloneState();
            Assert.That(after.Revision, Is.EqualTo(before.Revision));
            Assert.That(after.RandomState, Is.EqualTo(before.RandomState));
            Assert.That(after.Players["p1"].Hand, Is.EqualTo(before.Players["p1"].Hand));
            Assert.That(after.Players["p1"].CardActionsUsed, Is.Zero);
            var invalid = fixture.Rules.Execute(new GameCommand(GameCommandKind.PlayCards, "p1", sourceIds: new[] { "c1" },
                selections: new Dictionary<string, string[]> { ["$attack"] = new[] { "unknown" } }));
            Assert.That(invalid.Code, Is.EqualTo(CommandResultCode.Rejected));
            Assert.That(fixture.Data.Revision, Is.EqualTo(before.Revision));
            var valid = fixture.Rules.Execute(new GameCommand(GameCommandKind.PlayCards, "p1", sourceIds: new[] { "c1" },
                selections: new Dictionary<string, string[]> { ["$attack"] = new[] { "m2" } }));
            Assert.That(valid.Accepted, Is.True, valid.Message);
            Assert.That(fixture.Data.CloneState().Battle.Monsters["m2"].Hp, Is.EqualTo(28), "A fixed skill attack must not add the avatar's base attack.");
        }

        [Test]
        public void AvatarAttackFormula_IsAuthoredInsteadOfImplicitlyAdded()
        {
            using var fixture = new Fixture(State(), Catalog(
                cards: new[] { new CardSpec("strike", CardKind.Skill, attackValueId: "avatar-attack") },
                values: new[] { new ValueSpec("avatar-attack", ValueKind.PlayerBaseAttack) }));
            var result = fixture.Execute(GameCommandKind.PlayCards, "p1", "c1");
            Assert.That(result.Accepted, Is.True, result.Message);
            Assert.That(fixture.Data.CloneState().Battle.Monsters["m1"].Hp, Is.EqualTo(23));
        }

        [Test]
        public void MonsterResponse_AggregatesDamageBeforeRecipientMitigation()
        {
            var state = ResponseState(monsters: 2);
            state.Modifiers.Add(Modifier("guard-instance", "guard", "p1"));
            using var fixture = new Fixture(state, Catalog(monsterCount: 2,
                modifiers: new[] { new ModifierSpec("guard", ModifierKind.Stat, "permanent", StatKind.DamageTaken, amount: -1) }));
            var result = fixture.Execute(GameCommandKind.DeclineSwitch, "p2");
            Assert.That(result.Accepted, Is.True, result.Message);
            Assert.That(fixture.Data.CloneState().Players["p1"].Hp, Is.EqualTo(17), "2 + 2 damage receives one -1 reduction.");
        }

        [TestCase(false, 18)]
        [TestCase(true, 20)]
        public void SleepLabelAloneDoesNotBlockAttack_ExplicitActionModifierDoes(bool blocker, int expectedHp)
        {
            var state = ResponseState();
            state.Modifiers.Add(Modifier("sleep-instance", "sleep", "m1", anchor: "p1"));
            var definition = blocker
                ? new ModifierSpec("sleep", ModifierKind.PreventAction, "permanent", blockedAction: ActionKind.Attack, tags: new[] { "sleep" })
                : new ModifierSpec("sleep", ModifierKind.Stat, "permanent", tags: new[] { "sleep" });
            using var fixture = new Fixture(state, Catalog(modifiers: new[] { definition }));
            var result = fixture.Execute(GameCommandKind.DeclineSwitch, "p2");
            Assert.That(result.Accepted, Is.True, result.Message);
            Assert.That(fixture.Data.CloneState().Players["p1"].Hp, Is.EqualTo(expectedHp));
        }

        [Test]
        public void PlayerAttackBlock_SkipsNativeAttackButPreservesTheSkillEffect()
        {
            var state = State();
            state.Modifiers.Add(Modifier("attack-block-instance", "attack-block", "p1"));
            using var fixture = new Fixture(state, Catalog(
                cards: new[] { new CardSpec("strike", CardKind.Skill, 2, "earn-group") },
                groups: new[] { new EffectGroupSpec("earn-group") },
                effects: new[] { new EffectSpec("earn", "earn-group", 0, EffectOperation.GainResource, "self", 2, resourceKey: "coins") },
                modifiers: new[] { new ModifierSpec("attack-block", ModifierKind.PreventAction, "permanent", blockedAction: ActionKind.Attack) }));
            var result = fixture.Execute(GameCommandKind.PlayCards, "p1", "c1");
            AssertAccepted(result);
            var after = fixture.Data.CloneState();
            Assert.That(after.Players["p1"].Resources["coins"], Is.EqualTo(2));
            Assert.That(after.Battle.Monsters["m1"].Hp, Is.EqualTo(30));
            Assert.That(result.Events.Any(value => value.Kind == RuleEvents.BeforeAttack || value.Kind == RuleEvents.AfterAttack), Is.False);
            Assert.That(after.Cards["c1"].Zone, Is.EqualTo(CardZone.Discard));
        }

        [Test]
        public void ZeroAttackSupportSkill_DoesNotRequestAnUnnecessaryMonsterTarget()
        {
            using var fixture = new Fixture(State(monsters: 2), Catalog(monsterCount: 2,
                cards: new[] { new CardSpec("strike", CardKind.Skill, 0, "earn-group") },
                groups: new[] { new EffectGroupSpec("earn-group") },
                effects: new[] { new EffectSpec("earn", "earn-group", 0, EffectOperation.GainResource, "self", 2, resourceKey: "coins") }));
            var result = fixture.Execute(GameCommandKind.PlayCards, "p1", "c1");
            AssertAccepted(result);
            Assert.That(result.Choice, Is.Null);
            Assert.That(result.Events.Any(value => value.Kind == RuleEvents.BeforeAttack || value.Kind == RuleEvents.AfterAttack), Is.False);
            Assert.That(fixture.Data.CloneState().Battle.Step, Is.EqualTo(BattleStep.SwitchWindow));
        }

        [Test]
        public void OwnerStartExpiry_PrecedesStartTriggeredDamage()
        {
            var state = ResponseState();
            state.Modifiers.Add(Modifier("protection-instance", "protection", "p2", remaining: 1));
            state.Modifiers.Add(Modifier("tick-instance", "tick", "p2"));
            using var fixture = new Fixture(state, Catalog(monsterAttack: 0,
                groups: new[] { new EffectGroupSpec("tick-group") },
                effects: new[] { new EffectSpec("tick-damage", "tick-group", 0, EffectOperation.Damage, "self", 3) },
                durations: new[] { new DurationSpec("next-start", DurationKind.OwnerTurnStart) },
                modifiers: new[]
                {
                    new ModifierSpec("protection", ModifierKind.Stat, "next-start", StatKind.DamageTaken, StatOperation.Set, 0),
                    new ModifierSpec("tick", ModifierKind.PeriodicTrigger, "permanent", effectGroupId: "tick-group", trigger: GameEventKind.TurnStart)
                }));
            var result = fixture.Execute(GameCommandKind.DeclineSwitch, "p2");
            Assert.That(result.Accepted, Is.True, result.Message);
            var after = fixture.Data.CloneState();
            Assert.That(after.Players["p2"].Hp, Is.EqualTo(17));
            Assert.That(after.Modifiers.Any(modifier => modifier.SpecId == "protection"), Is.False);
        }

        [Test]
        public void PeriodicHealing_CountsOnlyAnchoredPersonalTurns()
        {
            var state = ResponseState();
            state.Players["p1"].Hp = 10;
            state.Modifiers.Add(Modifier("regen-instance", "regen", "p1"));
            using var fixture = new Fixture(state, Catalog(monsterAttack: 0,
                groups: new[] { new EffectGroupSpec("heal-group") },
                effects: new[] { new EffectSpec("heal", "heal-group", 0, EffectOperation.Heal, "self", 3) },
                modifiers: new[] { new ModifierSpec("regen", ModifierKind.PeriodicTrigger, "permanent", effectGroupId: "heal-group", trigger: GameEventKind.TurnStart, everyOccurrences: 2) }));
            AssertAccepted(fixture.Execute(GameCommandKind.DeclineSwitch, "p2"));
            Assert.That(fixture.Data.CloneState().Modifiers[0].TriggerOccurrences, Is.Zero);
            AssertAccepted(fixture.Execute(GameCommandKind.Pass, "p2"));
            AssertAccepted(fixture.Execute(GameCommandKind.DeclineSwitch, "p1"));
            Assert.That(fixture.Data.CloneState().Players["p1"].Hp, Is.EqualTo(10));
            AssertAccepted(fixture.Execute(GameCommandKind.Pass, "p1"));
            AssertAccepted(fixture.Execute(GameCommandKind.DeclineSwitch, "p2"));
            AssertAccepted(fixture.Execute(GameCommandKind.Pass, "p2"));
            AssertAccepted(fixture.Execute(GameCommandKind.DeclineSwitch, "p1"));
            Assert.That(fixture.Data.CloneState().Players["p1"].Hp, Is.EqualTo(13));
            Assert.That(fixture.Data.CloneState().Modifiers[0].TriggerOccurrences, Is.EqualTo(2));
        }

        [Test]
        public void BeforeDefeatReaction_CanPreventCooperativeLoss()
        {
            var state = ResponseState();
            state.Modifiers.Add(Modifier("rescue-instance", "rescue", "p1"));
            using var fixture = new Fixture(state, Catalog(monsterAttack: 100,
                groups: new[] { new EffectGroupSpec("rescue-group") },
                effects: new[] { new EffectSpec("rescue-heal", "rescue-group", 0, EffectOperation.Heal, "self", 1) },
                modifiers: new[] { new ModifierSpec("rescue", ModifierKind.PeriodicTrigger, "permanent", effectGroupId: "rescue-group", trigger: GameEventKind.BeforeDefeat) }));
            AssertAccepted(fixture.Execute(GameCommandKind.DeclineSwitch, "p2"));
            Assert.That(fixture.Data.CloneState().Defeated, Is.False);
            Assert.That(fixture.Data.CloneState().Players["p1"].Hp, Is.EqualTo(1));
        }

        [Test]
        public void RecursiveTriggeredEffect_ExhaustsBudgetWithoutCommittingAnyMutation()
        {
            var state = ResponseState();
            state.Modifiers.Add(Modifier("loop-instance", "loop", "p1"));
            using var fixture = new Fixture(state, Catalog(budget: 40,
                groups: new[] { new EffectGroupSpec("loop-group") },
                effects: new[] { new EffectSpec("loop-damage", "loop-group", 0, EffectOperation.Damage, "self", 1) },
                modifiers: new[] { new ModifierSpec("loop", ModifierKind.PeriodicTrigger, "permanent", effectGroupId: "loop-group", trigger: GameEventKind.BeforeDamage) }));
            var revision = fixture.Data.Revision;
            var result = fixture.Execute(GameCommandKind.DeclineSwitch, "p2");
            Assert.That(result.Code, Is.EqualTo(CommandResultCode.Rejected));
            Assert.That(result.Message, Does.Contain("budget"));
            Assert.That(fixture.Data.Revision, Is.EqualTo(revision));
            var after = fixture.Data.CloneState();
            Assert.That(after.Players["p1"].Hp, Is.EqualTo(20));
            Assert.That(after.Counters, Is.Empty);
            Assert.That(after.Battle.Step, Is.EqualTo(BattleStep.SwitchWindow));
        }

        [Test]
        public void EndRound_ClearsRoundStatusesResetsMonsterDamageAndDrawsBeforeReshuffle()
        {
            var state = State();
            state.PhaseId = "end";
            state.Battle.Completed = true;
            state.Battle.Step = BattleStep.Completed;
            state.Battle.Monsters["m1"].Hp = 7;
            state.Modifiers.Add(Modifier("paralysis-instance", "paralysis", "p1", remaining: 1));
            AddCard(state, "c2", "p1", CardZone.Deck);
            AddCard(state, "c3", "p1", CardZone.Discard);
            using var fixture = new Fixture(state, Catalog(draw: 2,
                durations: new[] { new DurationSpec("round", DurationKind.RoundEnd) },
                modifiers: new[] { new ModifierSpec("paralysis", ModifierKind.PreventAction, "round", blockedAction: ActionKind.Switch, tags: new[] { "paralysis" }) }));
            AssertAccepted(fixture.Execute(GameCommandKind.EndRound));
            var after = fixture.Data.CloneState();
            Assert.That(after.Modifiers, Is.Empty);
            Assert.That(after.Battle.Monsters["m1"].Hp, Is.EqualTo(30));
            Assert.That(after.Players["p1"].Hand.Count, Is.EqualTo(2));
            Assert.That(after.Players["p1"].Hand[0], Is.EqualTo("c2"));
            Assert.That(after.Players["p1"].Deck.Count, Is.EqualTo(1));
            Assert.That(after.Round, Is.EqualTo(2));
            Assert.That(after.LeaderPlayerId, Is.EqualTo("p2"));
        }

        [Test]
        public void UpgradeAttachesEffectGroupAndAttackBonusWithoutCardIdentityBranches()
        {
            var state = State();
            state.PhaseId = "town";
            state.Battle.Completed = true;
            state.Battle.Step = BattleStep.Completed;
            using var fixture = new Fixture(state, Catalog(
                groups: new[] { new EffectGroupSpec("upgrade-group") },
                effects: new[] { new EffectSpec("earn", "upgrade-group", 0, EffectOperation.GainResource, "self", 2, resourceKey: "coins") },
                upgrades: new[] { new UpgradeSpec("growth", effectGroupId: "upgrade-group", attackBonus: 1) }));
            AssertAccepted(fixture.Rules.Execute(new GameCommand(GameCommandKind.UpgradeCard, "p1", "growth", new[] { "c1" })));
            AssertAccepted(fixture.Rules.Execute(new GameCommand(GameCommandKind.StartBattle, specId: "encounter")));
            AssertAccepted(fixture.Execute(GameCommandKind.PlayCards, "p1", "c1"));
            var after = fixture.Data.CloneState();
            Assert.That(after.Players["p1"].Resources["coins"], Is.EqualTo(2));
            Assert.That(after.Battle.Monsters.Values.Single().Hp, Is.EqualTo(27));
        }

        [Test]
        public void TownHealing_RejectsNoEffectWithoutConsumingTheItem()
        {
            var state = State();
            state.PhaseId = "town";
            state.Battle.Completed = true;
            state.Battle.Step = BattleStep.Completed;
            using var fixture = new Fixture(state, Catalog(
                cards: new[] { new CardSpec("strike", CardKind.Item, effectGroupId: "heal-group", allowedPhases: new[] { PhaseKind.Town }, isEmergency: true) },
                groups: new[] { new EffectGroupSpec("heal-group") },
                effects: new[] { new EffectSpec("heal", "heal-group", 0, EffectOperation.Heal, "self", 3) }));
            var result = fixture.Execute(GameCommandKind.PlayCards, "p1", "c1");
            Assert.That(result.Code, Is.EqualTo(CommandResultCode.Rejected));
            Assert.That(fixture.Data.CloneState().Cards["c1"].Zone, Is.EqualTo(CardZone.Hand));
        }

        private static void AssertAccepted(CommandResult result) => Assert.That(result.Accepted, Is.True, result.Message);

        private static GameState ResponseState(int monsters = 1)
        {
            var state = State(monsters);
            state.Battle.Step = BattleStep.SwitchWindow;
            state.Battle.PendingResponsePlayerId = "p1";
            return state;
        }

        private static GameState State(int monsters = 1)
        {
            var state = new GameState { PhaseId = "combat", TurnSerial = 1, PhaseSerial = 1, CurrentPlayerId = "p1", LeaderPlayerId = "p1", NextInstanceId = 100 };
            foreach (var id in new[] { "p1", "p2" })
            {
                state.PlayerOrder.Add(id);
                state.Players.Add(id, new PlayerState { Id = id, ActorSpecId = "actor", Team = "players", Hp = 20, MaxHp = 20, BaseAttack = 7 });
                state.Battle.ParticipantIds.Add(id);
            }
            state.Battle.EncounterId = "encounter";
            state.Battle.Step = BattleStep.PlayerAction;
            for (var i = 1; i <= monsters; i++)
            {
                var id = "m" + i;
                state.Battle.MonsterOrder.Add(id);
                state.Battle.Monsters.Add(id, new MonsterState { Id = id, SpecId = "monster", Hp = 30, MaxHp = 30 });
            }
            AddCard(state, "c1", "p1", CardZone.Hand);
            return state;
        }

        private static void AddCard(GameState state, string id, string owner, CardZone zone)
        {
            state.Cards.Add(id, new CardInstanceState { Id = id, OwnerId = owner, SpecId = "strike", Zone = zone });
            var player = state.Players[owner];
            (zone == CardZone.Hand ? player.Hand : zone == CardZone.Deck ? player.Deck : player.Discard).Add(id);
        }

        private static ModifierState Modifier(string id, string specId, string target, int remaining = 0, string anchor = null) => new()
        {
            Id = id, SpecId = specId, TargetId = target, OwnerId = anchor ?? target, AnchorPlayerId = anchor ?? target,
            RemainingOccurrences = remaining, AppliedTurnSerial = 0, AppliedPhaseSerial = 0, AppliedRound = 1
        };

        private static GameSpecCatalog Catalog(int monsterCount = 1, int monsterAttack = 2, int draw = 0, int budget = 1024,
            IEnumerable<CardSpec> cards = null, IEnumerable<EffectGroupSpec> groups = null, IEnumerable<EffectSpec> effects = null,
            IEnumerable<ValueSpec> values = null, IEnumerable<DurationSpec> durations = null,
            IEnumerable<ModifierSpec> modifiers = null, IEnumerable<UpgradeSpec> upgrades = null) => new(
            new GameRulesSpec("combat", normalDrawCount: draw, soloDrawCount: draw, maxResolutionSteps: budget, combatPhaseId: "combat"),
            phases: new[] { new PhaseSpec("combat", PhaseKind.Combat, "end"), new PhaseSpec("end", PhaseKind.End, "combat"), new PhaseSpec("town", PhaseKind.Town, "explore"), new PhaseSpec("explore", PhaseKind.Exploration, "combat") },
            actors: new[] { new ActorSpec("actor", 20, 7) }, cards: cards ?? new[] { new CardSpec("strike", CardKind.Skill, 2) },
            groups: groups, effects: effects,
            selectors: new[] { new TargetSelectorSpec("self", new[] { TargetKind.Self }), new TargetSelectorSpec("current", new[] { TargetKind.CurrentPlayer }) },
            values: values, durations: new[] { new DurationSpec("permanent", DurationKind.Permanent) }.Concat(durations ?? Array.Empty<DurationSpec>()),
            modifiers: modifiers, upgrades: upgrades, monsters: new[] { new MonsterSpec("monster", 30, attack: monsterAttack) },
            encounters: new[] { new EncounterSpec("encounter", Enumerable.Repeat("monster", monsterCount)) });

        private sealed class Fixture : IDisposable
        {
            private readonly IScope _scope;
            public GameData Data { get; }
            public IGameRulesDomain Rules { get; }
            public Fixture(GameState state, GameSpecCatalog specs)
            {
                var builder = new ScopeBuilder();
                builder.Bind<GameData>().FromFactory(() => new GameData(state)).AsScoped();
                builder.Bind<IGameSpecs>().FromInstance(specs);
                builder.Bind<IGameRulesDomain>().To<GameRulesDomain>().AsScoped();
                _scope = builder.Build(name: "DomainTests");
                Data = _scope.Resolve<GameData>();
                Rules = _scope.Resolve<IGameRulesDomain>();
            }
            public CommandResult Execute(GameCommandKind kind, string actor = null, string source = null) =>
                Rules.Execute(new GameCommand(kind, actor, sourceIds: source == null ? null : new[] { source }));
            public void Dispose() => _scope.Dispose();
        }
    }
}
