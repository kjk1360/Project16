using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Project16.Foundation.Specs;

namespace Project16.CardGame
{
    public enum CardKind { Skill, Item, Equipment }
    public enum CardZone { Deck, Hand, Discard, Removed, Equipped }
    public enum BattleStep { None, PlayerAction, SwitchWindow, MonsterResponse, Completed }
    public enum ActionKind { PlayCard, Switch, Pass, Flee, Rest, Equip, Explore, Attack }
    public enum EffectOperation { Damage, Heal, GainResource, Draw, MoveCard, ApplyModifier, RemoveModifier, ExtraAttack, CancelResponse, RunGroup, AddCard, AttachUpgrade, RemoveModifiersByTag }
    public enum TargetKind { Self, SourceOwner, CurrentPlayer, NextPlayer, PreviousPlayer, SelectedPlayer, SelectedMonster, AllPlayers, BattleParticipants, AllMonsters, SourceCard, SelectedCard }
    public enum StatKind { Attack, DamageTaken, MaxHp, CardActions, ItemActions }
    public enum StatOperation { Add, MultiplyPercent, Set, Minimum, Maximum }
    public enum ModifierKind { Stat, PreventAction, PeriodicTrigger }
    public enum StackPolicy { Independent, Reject, Replace, Refresh, Add }
    public enum GameEventKind { None, PhaseStart, PhaseEnd, TurnStart, TurnEnd, CardUsed, BeforeAttack, AfterAttack, BeforeDamage, AfterDamage, BeforeDefeat, Defeated, SwitchAttempt, SwitchSucceeded, SwitchFailed, MonsterDefeated, BattleStart, BattleEnd, RoundEnd }
    public enum DurationKind { Permanent, CurrentTurn, OwnerTurnStart, OwnerTurnEnd, GlobalPhaseStart, GlobalPhaseEnd, BattleEnd, RoundEnd }
    public enum DurationAnchor { Owner, SourceOwner, CurrentPlayer }
    public enum ValueKind { Constant, SourceAttack, TargetHp, TargetMissingHp, TargetMaxHp, SourceResource, TargetResource, PlayerCount, BattleParticipantCount, Counter, PlayerBaseAttack }
    public enum ConditionKind { Always, All, Any, Not, HasTag, MissingTag, HpBelow, HpAtMost, HpAtLeast, ResourceAtLeast, PhaseIs, EventIs, CounterAtLeast }
    public enum PhaseKind { Start, Town, Exploration, Combat, End }

    internal static class SpecLists
    {
        public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) =>
            new ReadOnlyCollection<T>(values == null ? new List<T>() : new List<T>(values));
    }

    public readonly struct CompanionSpec : ISpecRow
    {
        public CompanionSpec(string key, string name = null, IEnumerable<string> tags = null)
        { Key = key; Name = name ?? key; Tags = SpecLists.Copy(tags); }
        public string Key { get; }
        public string Name { get; }
        public IReadOnlyList<string> Tags { get; }
    }

    public readonly struct ActorSpec : ISpecRow
    {
        public ActorSpec(string key, int maxHp, int baseAttack = 0, string team = "players", IEnumerable<string> tags = null)
        { Key = key; MaxHp = maxHp; BaseAttack = baseAttack; Team = team; Tags = SpecLists.Copy(tags); }
        public string Key { get; }
        public int MaxHp { get; }
        public int BaseAttack { get; }
        public string Team { get; }
        public IReadOnlyList<string> Tags { get; }
    }

    public readonly struct CardSpec : ISpecRow
    {
        public CardSpec(string key, CardKind kind, int attack = 0, string effectGroupId = null,
            string companionId = null, int cost = 0, string resourceKey = null, bool canSwitch = false,
            IEnumerable<string> tags = null, string attackValueId = null, IEnumerable<PhaseKind> allowedPhases = null, bool isEmergency = false)
        { Key = key; Kind = kind; Attack = attack; EffectGroupId = effectGroupId; CompanionId = companionId;
          Cost = cost; ResourceKey = resourceKey; CanSwitch = canSwitch; Tags = SpecLists.Copy(tags);
          AttackValueId = attackValueId; AllowedPhases = SpecLists.Copy(allowedPhases ?? new[] { PhaseKind.Combat }); IsEmergency = isEmergency; }
        public string Key { get; }
        public CardKind Kind { get; }
        public int Attack { get; }
        public string CompanionId { get; }
        public int Cost { get; }
        public string ResourceKey { get; }
        public string EffectGroupId { get; }
        public bool CanSwitch { get; }
        public IReadOnlyList<string> Tags { get; }
        public string AttackValueId { get; }
        public IReadOnlyList<PhaseKind> AllowedPhases { get; }
        public bool IsEmergency { get; }
    }

    public readonly struct EffectGroupSpec : ISpecRow
    {
        public EffectGroupSpec(string key) { Key = key; }
        public string Key { get; }
    }

    public readonly struct EffectSpec : ISpecRow
    {
        public EffectSpec(string key, string groupId, int order, EffectOperation operation, string targetId,
            int amount = 0, string valueId = null, string conditionId = null, string referenceId = null,
            string resourceKey = null, CardZone destination = CardZone.Discard, GameEventKind trigger = GameEventKind.None)
        { Key = key; GroupId = groupId; Order = order; Operation = operation; TargetId = targetId; Amount = amount;
          ValueId = valueId; ConditionId = conditionId; ReferenceId = referenceId; ResourceKey = resourceKey;
          Destination = destination; Trigger = trigger; }
        public string Key { get; }
        public string GroupId { get; }
        public int Order { get; }
        public EffectOperation Operation { get; }
        public string TargetId { get; }
        public int Amount { get; }
        public string ValueId { get; }
        public string ConditionId { get; }
        public string ReferenceId { get; }
        public string ResourceKey { get; }
        public CardZone Destination { get; }
        public GameEventKind Trigger { get; }
    }

    public readonly struct TargetSelectorSpec : ISpecRow
    {
        public TargetSelectorSpec(string key, IEnumerable<TargetKind> targets, bool deduplicateTargets = false,
            string requiredTag = null, string team = null)
        { Key = key; Targets = SpecLists.Copy(targets); DeduplicateTargets = deduplicateTargets; RequiredTag = requiredTag; Team = team; }
        public string Key { get; }
        public IReadOnlyList<TargetKind> Targets { get; }
        public bool DeduplicateTargets { get; }
        public string RequiredTag { get; }
        public string Team { get; }
    }

    public readonly struct ValueSpec : ISpecRow
    {
        public ValueSpec(string key, ValueKind kind, int amount = 0, string referenceId = null, int multiplier = 1, int divisor = 1)
        { Key = key; Kind = kind; Amount = amount; ReferenceId = referenceId; Multiplier = multiplier; Divisor = divisor; }
        public string Key { get; }
        public ValueKind Kind { get; }
        public int Amount { get; }
        public string ReferenceId { get; }
        public int Multiplier { get; }
        public int Divisor { get; }
    }

    public readonly struct ConditionSpec : ISpecRow
    {
        public ConditionSpec(string key, ConditionKind kind, int amount = 0, string referenceId = null,
            IEnumerable<string> children = null, GameEventKind eventKind = GameEventKind.None)
        { Key = key; Kind = kind; Amount = amount; ReferenceId = referenceId; Children = SpecLists.Copy(children); EventKind = eventKind; }
        public string Key { get; }
        public ConditionKind Kind { get; }
        public int Amount { get; }
        public string ReferenceId { get; }
        public IReadOnlyList<string> Children { get; }
        public GameEventKind EventKind { get; }
    }

    public readonly struct DurationSpec : ISpecRow
    {
        public DurationSpec(string key, DurationKind kind, int occurrences = 1, string phaseId = null,
            DurationAnchor anchor = DurationAnchor.Owner)
        { Key = key; Kind = kind; Occurrences = occurrences; PhaseId = phaseId; Anchor = anchor; }
        public string Key { get; }
        public DurationKind Kind { get; }
        public int Occurrences { get; }
        public string PhaseId { get; }
        public DurationAnchor Anchor { get; }
    }

    public readonly struct ModifierSpec : ISpecRow
    {
        public ModifierSpec(string key, ModifierKind kind, string durationId, StatKind stat = StatKind.Attack,
            StatOperation operation = StatOperation.Add, int amount = 0, string effectGroupId = null,
            GameEventKind trigger = GameEventKind.None, int everyOccurrences = 1, string conditionId = null,
            StackPolicy stackPolicy = StackPolicy.Refresh, int maxStacks = 1, ActionKind blockedAction = ActionKind.PlayCard,
            IEnumerable<string> tags = null, int priority = 0)
        { Key = key; Kind = kind; DurationId = durationId; Stat = stat; Operation = operation; Amount = amount;
          EffectGroupId = effectGroupId; Trigger = trigger; EveryOccurrences = everyOccurrences; ConditionId = conditionId;
          StackPolicy = stackPolicy; MaxStacks = maxStacks; BlockedAction = blockedAction; Tags = SpecLists.Copy(tags); Priority = priority; }
        public string Key { get; }
        public ModifierKind Kind { get; }
        public string DurationId { get; }
        public StatKind Stat { get; }
        public StatOperation Operation { get; }
        public int Amount { get; }
        public string EffectGroupId { get; }
        public GameEventKind Trigger { get; }
        public int EveryOccurrences { get; }
        public string ConditionId { get; }
        public StackPolicy StackPolicy { get; }
        public int MaxStacks { get; }
        public ActionKind BlockedAction { get; }
        public IReadOnlyList<string> Tags { get; }
        public int Priority { get; }
    }

    public readonly struct UpgradeSpec : ISpecRow
    {
        public UpgradeSpec(string key, string effectGroupId = null, string modifierId = null, string requiredCardTag = null,
            string companionId = null, int attackBonus = 0, int maxAttachments = 1)
        { Key = key; EffectGroupId = effectGroupId; ModifierId = modifierId; RequiredCardTag = requiredCardTag;
          CompanionId = companionId; AttackBonus = attackBonus; MaxAttachments = maxAttachments; }
        public string Key { get; }
        public string EffectGroupId { get; }
        public string ModifierId { get; }
        public string RequiredCardTag { get; }
        public string CompanionId { get; }
        public int AttackBonus { get; }
        public int MaxAttachments { get; }
    }

    public readonly struct MonsterSpec : ISpecRow
    {
        public MonsterSpec(string key, int maxHp, string attackGroupId = null, int attack = 0,
            string rewardId = null, string finishRewardId = null, IEnumerable<string> tags = null, bool repeatable = false, string maxHpValueId = null)
        { Key = key; MaxHp = maxHp; AttackGroupId = attackGroupId; Attack = attack; RewardId = rewardId;
          FinishRewardId = finishRewardId; Tags = SpecLists.Copy(tags); Repeatable = repeatable; MaxHpValueId = maxHpValueId; }
        public string Key { get; }
        public int MaxHp { get; }
        public string AttackGroupId { get; }
        public int Attack { get; }
        public string RewardId { get; }
        public string FinishRewardId { get; }
        public IReadOnlyList<string> Tags { get; }
        public bool Repeatable { get; }
        public string MaxHpValueId { get; }
    }

    public readonly struct EncounterSpec : ISpecRow
    {
        public EncounterSpec(string key, IEnumerable<string> monsterIds, string rewardId = null,
            string nextEncounterId = null, bool victoryOnClear = false, string enterGroupId = null)
        { Key = key; MonsterIds = SpecLists.Copy(monsterIds); RewardId = rewardId; NextEncounterId = nextEncounterId;
          VictoryOnClear = victoryOnClear; EnterGroupId = enterGroupId; }
        public string Key { get; }
        public IReadOnlyList<string> MonsterIds { get; }
        public string RewardId { get; }
        public string NextEncounterId { get; }
        public bool VictoryOnClear { get; }
        public string EnterGroupId { get; }
    }

    public readonly struct RewardSpec : ISpecRow
    {
        public RewardSpec(string key, string effectGroupId) { Key = key; EffectGroupId = effectGroupId; }
        public string Key { get; }
        public string EffectGroupId { get; }
    }

    public readonly struct DeckEntrySpec : ISpecRow
    {
        public DeckEntrySpec(string key, string deckId, string cardId, int count, int order = 0)
        { Key = key; DeckId = deckId; CardId = cardId; Count = count; Order = order; }
        public string Key { get; }
        public string DeckId { get; }
        public string CardId { get; }
        public int Count { get; }
        public int Order { get; }
    }

    public readonly struct PhaseSpec : ISpecRow
    {
        public PhaseSpec(string key, PhaseKind kind, string nextPhaseId = null, string enterGroupId = null, string exitGroupId = null)
        { Key = key; Kind = kind; NextPhaseId = nextPhaseId; EnterGroupId = enterGroupId; ExitGroupId = exitGroupId; }
        public string Key { get; }
        public PhaseKind Kind { get; }
        public string NextPhaseId { get; }
        public string EnterGroupId { get; }
        public string ExitGroupId { get; }
    }

    public readonly struct ScenarioSpec : ISpecRow
    {
        public ScenarioSpec(string key, IEnumerable<string> actorIds, string deckId, string encounterId = null,
            IEnumerable<string> companionIds = null, bool isSolo = false)
        { Key = key; ActorIds = SpecLists.Copy(actorIds); DeckId = deckId; EncounterId = encounterId;
          CompanionIds = SpecLists.Copy(companionIds); IsSolo = isSolo; }
        public string Key { get; }
        public IReadOnlyList<string> ActorIds { get; }
        public string DeckId { get; }
        public string EncounterId { get; }
        public IReadOnlyList<string> CompanionIds { get; }
        public bool IsSolo { get; }
    }

    public readonly struct GameRulesSpec
    {
        public GameRulesSpec(string initialPhaseId, int normalDrawCount = 5, int soloDrawCount = 6,
            int cardActionsPerTurn = 1, int itemActionsPerTurn = 1, int maxResolutionSteps = 1024, string combatPhaseId = null,
            int restHeal = 1, int restWithExileHeal = 3)
        { InitialPhaseId = initialPhaseId; NormalDrawCount = normalDrawCount; SoloDrawCount = soloDrawCount;
          CardActionsPerTurn = cardActionsPerTurn; ItemActionsPerTurn = itemActionsPerTurn; MaxResolutionSteps = maxResolutionSteps; CombatPhaseId = combatPhaseId;
          RestHeal = restHeal; RestWithExileHeal = restWithExileHeal; }
        public string InitialPhaseId { get; }
        public int NormalDrawCount { get; }
        public int SoloDrawCount { get; }
        public int CardActionsPerTurn { get; }
        public int ItemActionsPerTurn { get; }
        public int MaxResolutionSteps { get; }
        public string CombatPhaseId { get; }
        public int RestHeal { get; }
        public int RestWithExileHeal { get; }
    }
}
