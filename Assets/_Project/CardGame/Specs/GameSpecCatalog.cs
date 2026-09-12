using System;
using System.Collections.Generic;
using System.Linq;
using Kylin.DI.Layered;
using Project16.Foundation.Specs;

namespace Project16.CardGame
{
    public interface IGameSpecs : IDataLayer
    {
        GameRulesSpec Rules { get; }
        ActorSpec Actor(string id);
        CompanionSpec Companion(string id);
        CardSpec Card(string id);
        EffectSpec Effect(string id);
        TargetSelectorSpec Selector(string id);
        ValueSpec Value(string id);
        ConditionSpec Condition(string id);
        DurationSpec Duration(string id);
        ModifierSpec Modifier(string id);
        UpgradeSpec Upgrade(string id);
        MonsterSpec Monster(string id);
        EncounterSpec Encounter(string id);
        RewardSpec Reward(string id);
        PhaseSpec Phase(string id);
        ScenarioSpec Scenario(string id);
        IReadOnlyList<EffectSpec> Effects(string groupId);
        IReadOnlyList<DeckEntrySpec> DeckEntries(string deckId);
    }

    /// <summary>Validated immutable composition snapshot; no live spreadsheet/provider is retained.</summary>
    public sealed class GameSpecCatalog : IGameSpecs
    {
        private readonly Dictionary<string, IReadOnlyList<EffectSpec>> _effects = new Dictionary<string, IReadOnlyList<EffectSpec>>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<DeckEntrySpec>> _decks = new Dictionary<string, IReadOnlyList<DeckEntrySpec>>(StringComparer.Ordinal);

        public GameSpecCatalog(GameRulesSpec rules, IEnumerable<PhaseSpec> phases = null,
            IEnumerable<ActorSpec> actors = null, IEnumerable<CompanionSpec> companions = null,
            IEnumerable<CardSpec> cards = null, IEnumerable<EffectGroupSpec> groups = null,
            IEnumerable<EffectSpec> effects = null, IEnumerable<TargetSelectorSpec> selectors = null,
            IEnumerable<ValueSpec> values = null, IEnumerable<ConditionSpec> conditions = null,
            IEnumerable<DurationSpec> durations = null, IEnumerable<ModifierSpec> modifiers = null,
            IEnumerable<UpgradeSpec> upgrades = null, IEnumerable<MonsterSpec> monsters = null,
            IEnumerable<EncounterSpec> encounters = null, IEnumerable<RewardSpec> rewards = null,
            IEnumerable<DeckEntrySpec> deckEntries = null, IEnumerable<ScenarioSpec> scenarios = null)
        {
            Rules = rules; Phases = Table(phases); Actors = Table(actors); Companions = Table(companions);
            Cards = Table(cards); Groups = Table(groups); EffectRows = Table(effects); Selectors = Table(selectors);
            Values = Table(values); Conditions = Table(conditions); Durations = Table(durations);
            Modifiers = Table(modifiers); Upgrades = Table(upgrades); Monsters = Table(monsters);
            Encounters = Table(encounters); Rewards = Table(rewards); DeckRows = Table(deckEntries); Scenarios = Table(scenarios);
            foreach (EffectGroupSpec group in Groups.Rows)
                _effects.Add(group.Key, SpecLists.Copy(EffectRows.Rows.Where(row => row.GroupId == group.Key).OrderBy(row => row.Order)));
            foreach (var deck in DeckRows.Rows.GroupBy(row => row.DeckId ?? string.Empty))
                _decks.Add(deck.Key, SpecLists.Copy(deck.OrderBy(row => row.Order).ThenBy(row => row.Key, StringComparer.Ordinal)));
            Validate();
        }

        public GameRulesSpec Rules { get; }
        public ISpecTable<PhaseSpec> Phases { get; }
        public ISpecTable<ActorSpec> Actors { get; }
        public ISpecTable<CompanionSpec> Companions { get; }
        public ISpecTable<CardSpec> Cards { get; }
        public ISpecTable<EffectGroupSpec> Groups { get; }
        public ISpecTable<EffectSpec> EffectRows { get; }
        public ISpecTable<TargetSelectorSpec> Selectors { get; }
        public ISpecTable<ValueSpec> Values { get; }
        public ISpecTable<ConditionSpec> Conditions { get; }
        public ISpecTable<DurationSpec> Durations { get; }
        public ISpecTable<ModifierSpec> Modifiers { get; }
        public ISpecTable<UpgradeSpec> Upgrades { get; }
        public ISpecTable<MonsterSpec> Monsters { get; }
        public ISpecTable<EncounterSpec> Encounters { get; }
        public ISpecTable<RewardSpec> Rewards { get; }
        public ISpecTable<DeckEntrySpec> DeckRows { get; }
        public ISpecTable<ScenarioSpec> Scenarios { get; }
        public ActorSpec Actor(string id) => Actors.Get(id);
        public CompanionSpec Companion(string id) => Companions.Get(id);
        public CardSpec Card(string id) => Cards.Get(id);
        public EffectSpec Effect(string id) => EffectRows.Get(id);
        public TargetSelectorSpec Selector(string id) => Selectors.Get(id);
        public ValueSpec Value(string id) => Values.Get(id);
        public ConditionSpec Condition(string id) => Conditions.Get(id);
        public DurationSpec Duration(string id) => Durations.Get(id);
        public ModifierSpec Modifier(string id) => Modifiers.Get(id);
        public UpgradeSpec Upgrade(string id) => Upgrades.Get(id);
        public MonsterSpec Monster(string id) => Monsters.Get(id);
        public EncounterSpec Encounter(string id) => Encounters.Get(id);
        public RewardSpec Reward(string id) => Rewards.Get(id);
        public PhaseSpec Phase(string id) => Phases.Get(id);
        public ScenarioSpec Scenario(string id) => Scenarios.Get(id);
        public IReadOnlyList<EffectSpec> Effects(string groupId) => string.IsNullOrEmpty(groupId) ? Array.Empty<EffectSpec>() : _effects[groupId];
        public IReadOnlyList<DeckEntrySpec> DeckEntries(string deckId) => _decks[deckId];

        // Composition copies plain row values into an immutable index; this creates no injected Data dependency.
#pragma warning disable KDI009
        private static ISpecTable<T> Table<T>(IEnumerable<T> rows) where T : struct, ISpecRow => new SpecTable<T>(rows ?? Array.Empty<T>());
#pragma warning restore KDI009

        private void Validate()
        {
            var errors = new List<string>();
            void Check(bool valid, string message) { if (!valid) errors.Add(message); }
            void Reference<T>(string id, ISpecTable<T> table, string context, bool optional = false) where T : struct, ISpecRow
            { if (optional && string.IsNullOrEmpty(id)) return; Check(table.TryGet(id, out _), context + ": missing reference '" + id + "'."); }
            void Defined<T>(T value, string context) where T : struct => Check(Enum.IsDefined(typeof(T), value), context + ": invalid " + typeof(T).Name + ".");
            void Tags(IReadOnlyList<string> tags, string context)
            { Check(tags != null && tags.All(tag => !string.IsNullOrWhiteSpace(tag)) && tags.Distinct(StringComparer.Ordinal).Count() == tags.Count, context + ": invalid/duplicate tags."); }

            Reference(Rules.InitialPhaseId, Phases, "Rules.InitialPhaseId");
            Reference(Rules.CombatPhaseId, Phases, "Rules.CombatPhaseId", true);
            Check(Rules.NormalDrawCount >= 0 && Rules.SoloDrawCount >= 0 && Rules.CardActionsPerTurn >= 0 && Rules.ItemActionsPerTurn >= 0 && Rules.MaxResolutionSteps > 0, "Rules: counts must be nonnegative and resolution bound positive.");
            Check(Rules.RestHeal >= 0 && Rules.RestWithExileHeal >= 0, "Rules: rest healing must be nonnegative.");
            foreach (PhaseSpec row in Phases.Rows)
            { Defined(row.Kind, row.Key); Reference(row.NextPhaseId, Phases, row.Key, true); Reference(row.EnterGroupId, Groups, row.Key, true); Reference(row.ExitGroupId, Groups, row.Key, true); }
            foreach (ActorSpec row in Actors.Rows)
            { Check(row.MaxHp > 0 && row.BaseAttack >= 0 && !string.IsNullOrWhiteSpace(row.Team), row.Key + ": invalid actor stats/team."); Tags(row.Tags, row.Key); }
            foreach (CompanionSpec row in Companions.Rows) Tags(row.Tags, row.Key);
            foreach (CardSpec row in Cards.Rows)
            {
                Defined(row.Kind, row.Key); Tags(row.Tags, row.Key); Reference(row.EffectGroupId, Groups, row.Key, true); Reference(row.CompanionId, Companions, row.Key, true);
                Reference(row.AttackValueId, Values, row.Key, true);
                if (!string.IsNullOrEmpty(row.AttackValueId) && Values.TryGet(row.AttackValueId, out ValueSpec attackValue))
                    Check(attackValue.Kind != ValueKind.SourceAttack, row.Key + ": AttackValueId cannot recursively evaluate the same source attack.");
                Check(row.AllowedPhases != null && row.AllowedPhases.Count > 0 && row.AllowedPhases.Distinct().Count() == row.AllowedPhases.Count, row.Key + ": card needs distinct allowed phases.");
                if (row.AllowedPhases != null) foreach (PhaseKind phase in row.AllowedPhases) Defined(phase, row.Key);
                Check(row.Attack >= 0 && row.Cost >= 0 && (row.Cost == 0 || !string.IsNullOrWhiteSpace(row.ResourceKey)), row.Key + ": invalid card attack/cost.");
            }
            foreach (TargetSelectorSpec row in Selectors.Rows)
            { Check(row.Targets != null && row.Targets.Count > 0, row.Key + ": empty target selector."); if (row.Targets != null) foreach (TargetKind target in row.Targets) Defined(target, row.Key); }
            foreach (ValueSpec row in Values.Rows)
            { Defined(row.Kind, row.Key); Check(row.Divisor > 0, row.Key + ": value divisor must be positive."); if (row.Kind == ValueKind.SourceResource || row.Kind == ValueKind.TargetResource || row.Kind == ValueKind.Counter) Check(!string.IsNullOrWhiteSpace(row.ReferenceId), row.Key + ": missing value reference."); }
            foreach (ConditionSpec row in Conditions.Rows)
            {
                Defined(row.Kind, row.Key); Defined(row.EventKind, row.Key);
                if (row.Kind == ConditionKind.All || row.Kind == ConditionKind.Any || row.Kind == ConditionKind.Not)
                { Check(row.Children != null && row.Children.Count > 0 && (row.Kind != ConditionKind.Not || row.Children.Count == 1), row.Key + ": invalid logical condition arity."); if (row.Children != null) foreach (string child in row.Children) Reference(child, Conditions, row.Key); }
                else Check(row.Children != null && row.Children.Count == 0, row.Key + ": leaf condition cannot have children.");
                if (row.Kind == ConditionKind.PhaseIs) Reference(row.ReferenceId, Phases, row.Key);
                if (row.Kind == ConditionKind.HasTag || row.Kind == ConditionKind.MissingTag || row.Kind == ConditionKind.ResourceAtLeast || row.Kind == ConditionKind.CounterAtLeast)
                    Check(!string.IsNullOrWhiteSpace(row.ReferenceId), row.Key + ": missing condition reference.");
                if (row.Kind == ConditionKind.EventIs) Check(row.EventKind != GameEventKind.None, row.Key + ": EventIs requires an event.");
            }
            foreach (DurationSpec row in Durations.Rows)
            {
                Defined(row.Kind, row.Key); Defined(row.Anchor, row.Key);
                Check(row.Kind == DurationKind.Permanent ? row.Occurrences >= 0 : row.Occurrences > 0, row.Key + ": invalid duration occurrences.");
                bool phase = row.Kind == DurationKind.GlobalPhaseStart || row.Kind == DurationKind.GlobalPhaseEnd;
                if (phase) Reference(row.PhaseId, Phases, row.Key); else Check(string.IsNullOrEmpty(row.PhaseId), row.Key + ": phase filter is only valid for global phase durations.");
            }
            foreach (ModifierSpec row in Modifiers.Rows)
            {
                Defined(row.Kind, row.Key); Defined(row.Stat, row.Key); Defined(row.Operation, row.Key); Defined(row.Trigger, row.Key); Defined(row.StackPolicy, row.Key); Defined(row.BlockedAction, row.Key);
                Reference(row.DurationId, Durations, row.Key); Reference(row.ConditionId, Conditions, row.Key, true); Reference(row.EffectGroupId, Groups, row.Key, true); Tags(row.Tags, row.Key);
                Check(row.MaxStacks > 0 && row.EveryOccurrences > 0, row.Key + ": invalid stack/periodic count.");
                if (row.Kind == ModifierKind.PeriodicTrigger) Check(row.Trigger != GameEventKind.None && !string.IsNullOrEmpty(row.EffectGroupId), row.Key + ": periodic modifier needs a trigger and group.");
                else Check(string.IsNullOrEmpty(row.EffectGroupId) && row.Trigger == GameEventKind.None, row.Key + ": stat/action modifier cannot also execute a trigger group.");
            }
            foreach (UpgradeSpec row in Upgrades.Rows)
            {
                Reference(row.EffectGroupId, Groups, row.Key, true); Reference(row.ModifierId, Modifiers, row.Key, true); Reference(row.CompanionId, Companions, row.Key, true); Check(row.MaxAttachments > 0, row.Key + ": invalid attachment limit.");
                if (!string.IsNullOrEmpty(row.ModifierId) && Modifiers.TryGet(row.ModifierId, out ModifierSpec intrinsic))
                    Check(intrinsic.Kind == ModifierKind.PreventAction || (intrinsic.Kind == ModifierKind.Stat && intrinsic.Stat == StatKind.Attack),
                        row.Key + ": intrinsic skill modifiers support Attack or PreventAction; attach other modifiers through an effect group with an explicit target/duration.");
            }
            foreach (MonsterSpec row in Monsters.Rows)
            { Check(row.MaxHp > 0 && row.Attack >= 0, row.Key + ": invalid monster stats."); Tags(row.Tags, row.Key); Reference(row.AttackGroupId, Groups, row.Key, true); Reference(row.RewardId, Rewards, row.Key, true); Reference(row.FinishRewardId, Rewards, row.Key, true); Reference(row.MaxHpValueId, Values, row.Key, true); }
            foreach (EncounterSpec row in Encounters.Rows)
            { Check(row.MonsterIds != null && row.MonsterIds.Count > 0, row.Key + ": encounter needs monsters."); if (row.MonsterIds != null) foreach (string monster in row.MonsterIds) Reference(monster, Monsters, row.Key); Reference(row.RewardId, Rewards, row.Key, true); Reference(row.NextEncounterId, Encounters, row.Key, true); Reference(row.EnterGroupId, Groups, row.Key, true); Check(!row.VictoryOnClear || string.IsNullOrEmpty(row.NextEncounterId), row.Key + ": victory and automatic next encounter conflict."); }
            foreach (RewardSpec row in Rewards.Rows) Reference(row.EffectGroupId, Groups, row.Key);
            foreach (DeckEntrySpec row in DeckRows.Rows)
            { Reference(row.CardId, Cards, row.Key); Check(row.Count > 0 && row.Order >= 0 && !string.IsNullOrWhiteSpace(row.DeckId), row.Key + ": invalid deck entry."); }
            foreach (ScenarioSpec row in Scenarios.Rows)
            {
                Check(row.ActorIds != null && row.ActorIds.Count > 0 && (!row.IsSolo || row.ActorIds.Count == 1), row.Key + ": invalid scenario actors.");
                if (row.ActorIds != null) foreach (string actor in row.ActorIds) Reference(actor, Actors, row.Key);
                if (row.CompanionIds != null) foreach (string companion in row.CompanionIds) Reference(companion, Companions, row.Key);
                Check(!string.IsNullOrEmpty(row.DeckId) && _decks.ContainsKey(row.DeckId), row.Key + ": missing deck."); Reference(row.EncounterId, Encounters, row.Key, true);
            }
            foreach (EffectSpec row in EffectRows.Rows)
            {
                Defined(row.Operation, row.Key); Defined(row.Destination, row.Key); Defined(row.Trigger, row.Key); Check(row.Order >= 0, row.Key + ": negative effect order.");
                Reference(row.GroupId, Groups, row.Key); Reference(row.TargetId, Selectors, row.Key); Reference(row.ValueId, Values, row.Key, true); Reference(row.ConditionId, Conditions, row.Key, true);
                if (string.IsNullOrEmpty(row.ValueId))
                {
                    if (row.Operation == EffectOperation.Damage || row.Operation == EffectOperation.Heal || row.Operation == EffectOperation.Draw || row.Operation == EffectOperation.ExtraAttack)
                        Check(row.Amount >= 0, row.Key + ": this operation requires a nonnegative amount.");
                    if (row.Operation == EffectOperation.AddCard) Check(row.Amount > 0, row.Key + ": AddCard requires a positive count.");
                }
                if (Selectors.TryGet(row.TargetId, out TargetSelectorSpec targetSelector) && targetSelector.Targets != null)
                {
                    bool cardTargets = targetSelector.Targets.All(target => target == TargetKind.SourceCard || target == TargetKind.SelectedCard);
                    bool hasCardTarget = targetSelector.Targets.Any(target => target == TargetKind.SourceCard || target == TargetKind.SelectedCard);
                    if (row.Operation == EffectOperation.MoveCard || row.Operation == EffectOperation.AttachUpgrade)
                        Check(cardTargets, row.Key + ": card movement/upgrades require a card selector.");
                    if (row.Operation == EffectOperation.Damage || row.Operation == EffectOperation.Heal || row.Operation == EffectOperation.Draw || row.Operation == EffectOperation.GainResource || row.Operation == EffectOperation.AddCard)
                        Check(!hasCardTarget, row.Key + ": this operation cannot target a card instance.");
                }
                switch (row.Operation)
                {
                    case EffectOperation.ApplyModifier: case EffectOperation.RemoveModifier: Reference(row.ReferenceId, Modifiers, row.Key); break;
                    case EffectOperation.RunGroup: Reference(row.ReferenceId, Groups, row.Key); break;
                    case EffectOperation.AddCard: Reference(row.ReferenceId, Cards, row.Key); break;
                    case EffectOperation.AttachUpgrade: Reference(row.ReferenceId, Upgrades, row.Key); break;
                    case EffectOperation.GainResource: Check(!string.IsNullOrWhiteSpace(row.ResourceKey), row.Key + ": resource effect has no key."); break;
                    case EffectOperation.RemoveModifiersByTag: Check(!string.IsNullOrWhiteSpace(row.ReferenceId), row.Key + ": cleanse effect needs a modifier tag."); break;
                }
            }
            foreach (var group in EffectRows.Rows.GroupBy(row => row.GroupId))
                Check(group.Select(row => row.Order).Distinct().Count() == group.Count(), group.Key + ": effect orders must be unique within a group.");

            DetectCycles(Conditions.Rows.Select(row => row.Key), id => Conditions.Get(id).Children ?? Array.Empty<string>(), "Condition", errors);
            DetectCycles(Groups.Rows.Select(row => row.Key), id => EffectRows.Rows.Where(row => row.GroupId == id && row.Operation == EffectOperation.RunGroup).Select(row => row.ReferenceId), "Effect group", errors);
            DetectCycles(Encounters.Rows.Select(row => row.Key), id => string.IsNullOrEmpty(Encounters.Get(id).NextEncounterId) ? Array.Empty<string>() : new[] { Encounters.Get(id).NextEncounterId }, "Encounter chain", errors);
            if (errors.Count > 0) throw new SpecValidationException(string.Join(Environment.NewLine, errors));
        }

        private static void DetectCycles(IEnumerable<string> keys, Func<string, IEnumerable<string>> next, string kind, List<string> errors)
        {
            var known = new HashSet<string>(keys, StringComparer.Ordinal);
            var active = new HashSet<string>(StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string key)
            {
                if (!known.Contains(key) || done.Contains(key)) return;
                if (!active.Add(key)) { errors.Add(kind + " cycle at '" + key + "'."); return; }
                foreach (string child in next(key)) if (child != null) Visit(child);
                active.Remove(key); done.Add(key);
            }
            foreach (string key in known) Visit(key);
        }
    }
}
