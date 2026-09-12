using System;
using System.Collections.Generic;
using Project16.Foundation.BGDatabase;
using Project16.Foundation.Specs;

namespace Project16.CardGame.Composition
{
    /// <summary>Explicit AOT-safe mapping; BG rows never cross this composition boundary.</summary>
    public static class GameSpecLoader
    {
        public static GameSpecCatalog Load(byte[] bytes)
        {
            using var source = BgSpecSource.Load(bytes);
            var header = source.ReadTable("catalog", r => new CatalogRow(r.Name, r.ReadInt("schemaVersion")));
            if (header.Count != 1 || header.Rows[0].Version != 1) throw new SpecValidationException("Unsupported card game Spec schema.");
            var rules = source.ReadTable("rules", r => new RulesRow(r.Name, new GameRulesSpec(r.ReadOptionalRelationKey("initialPhaseId", "phases"), r.ReadInt("normalDrawCount"), r.ReadInt("soloDrawCount"), r.ReadInt("cardActionsPerTurn"), r.ReadInt("itemActionsPerTurn"), r.ReadInt("maxResolutionSteps"), r.ReadOptionalRelationKey("combatPhaseId", "phases"), r.ReadInt("restHeal"), r.ReadInt("restWithExileHeal"))));
            if (rules.Count != 1) throw new SpecValidationException("Exactly one rules row is required.");
            return new GameSpecCatalog(rules.Rows[0].Value,
                phases: source.ReadTable("phases", r => new PhaseSpec(r.Name, EnumValue<PhaseKind>(r.ReadString("kind")), r.ReadOptionalRelationKey("nextPhaseId", "phases"), r.ReadOptionalRelationKey("enterGroupId", "groups"), r.ReadOptionalRelationKey("exitGroupId", "groups"))).Rows,
                actors: source.ReadTable("actors", r => new ActorSpec(r.Name, r.ReadInt("maxHp"), r.ReadInt("baseAttack"), r.ReadString("team"), r.ReadStrings("tags"))).Rows,
                companions: source.ReadTable("companions", r => new CompanionSpec(r.Name, r.ReadString("displayName"), r.ReadStrings("tags"))).Rows,
                cards: source.ReadTable("cards", r => new CardSpec(r.Name, EnumValue<CardKind>(r.ReadString("kind")), r.ReadInt("attack"), r.ReadOptionalRelationKey("effectGroupId", "groups"), r.ReadOptionalRelationKey("companionId", "companions"), r.ReadInt("cost"), r.ReadString("resourceKey"), r.ReadBool("canSwitch"), r.ReadStrings("tags"), r.ReadOptionalRelationKey("attackValueId", "values"), Enums<PhaseKind>(r.ReadStrings("allowedPhases")), r.ReadBool("isEmergency"))).Rows,
                groups: source.ReadTable("groups", r => new EffectGroupSpec(r.Name)).Rows,
                effects: source.ReadTable("effects", r => new EffectSpec(r.Name, r.ReadOptionalRelationKey("groupId", "groups"), r.ReadInt("order"), EnumValue<EffectOperation>(r.ReadString("operation")), r.ReadOptionalRelationKey("targetId", "selectors"), r.ReadInt("amount"), r.ReadOptionalRelationKey("valueId", "values"), r.ReadOptionalRelationKey("conditionId", "conditions"), r.ReadString("referenceId"), r.ReadString("resourceKey"), EnumValue<CardZone>(r.ReadString("destination")), EnumValue<GameEventKind>(r.ReadString("trigger")))).Rows,
                selectors: source.ReadTable("selectors", r => new TargetSelectorSpec(r.Name, Enums<TargetKind>(r.ReadStrings("targets")), r.ReadBool("deduplicateTargets"), r.ReadString("requiredTag"), r.ReadString("team"))).Rows,
                values: source.ReadTable("values", r => new ValueSpec(r.Name, EnumValue<ValueKind>(r.ReadString("kind")), r.ReadInt("amount"), r.ReadString("referenceId"), r.ReadInt("multiplier"), r.ReadInt("divisor"))).Rows,
                conditions: source.ReadTable("conditions", r => new ConditionSpec(r.Name, EnumValue<ConditionKind>(r.ReadString("kind")), r.ReadInt("amount"), r.ReadString("referenceId"), r.ReadRelationKeys("children", "conditions"), EnumValue<GameEventKind>(r.ReadString("eventKind")))).Rows,
                durations: source.ReadTable("durations", r => new DurationSpec(r.Name, EnumValue<DurationKind>(r.ReadString("kind")), r.ReadInt("occurrences"), r.ReadOptionalRelationKey("phaseId", "phases"), EnumValue<DurationAnchor>(r.ReadString("anchor")))).Rows,
                modifiers: source.ReadTable("modifiers", r => new ModifierSpec(r.Name, EnumValue<ModifierKind>(r.ReadString("kind")), r.ReadOptionalRelationKey("durationId", "durations"), EnumValue<StatKind>(r.ReadString("stat")), EnumValue<StatOperation>(r.ReadString("operation")), r.ReadInt("amount"), r.ReadOptionalRelationKey("effectGroupId", "groups"), EnumValue<GameEventKind>(r.ReadString("trigger")), r.ReadInt("everyOccurrences"), r.ReadOptionalRelationKey("conditionId", "conditions"), EnumValue<StackPolicy>(r.ReadString("stackPolicy")), r.ReadInt("maxStacks"), EnumValue<ActionKind>(r.ReadString("blockedAction")), r.ReadStrings("tags"), r.ReadInt("priority"))).Rows,
                upgrades: source.ReadTable("upgrades", r => new UpgradeSpec(r.Name, r.ReadOptionalRelationKey("effectGroupId", "groups"), r.ReadOptionalRelationKey("modifierId", "modifiers"), r.ReadString("requiredCardTag"), r.ReadOptionalRelationKey("companionId", "companions"), r.ReadInt("attackBonus"), r.ReadInt("maxAttachments"))).Rows,
                monsters: source.ReadTable("monsters", r => new MonsterSpec(r.Name, r.ReadInt("maxHp"), r.ReadOptionalRelationKey("attackGroupId", "groups"), r.ReadInt("attack"), r.ReadOptionalRelationKey("rewardId", "rewards"), r.ReadOptionalRelationKey("finishRewardId", "rewards"), r.ReadStrings("tags"), r.ReadBool("repeatable"), r.ReadOptionalRelationKey("maxHpValueId", "values"))).Rows,
                encounters: source.ReadTable("encounters", r => new EncounterSpec(r.Name, r.ReadRelationKeys("monsterIds", "monsters"), r.ReadOptionalRelationKey("rewardId", "rewards"), r.ReadOptionalRelationKey("nextEncounterId", "encounters"), r.ReadBool("victoryOnClear"), r.ReadOptionalRelationKey("enterGroupId", "groups"))).Rows,
                rewards: source.ReadTable("rewards", r => new RewardSpec(r.Name, r.ReadOptionalRelationKey("effectGroupId", "groups"))).Rows,
                deckEntries: source.ReadTable("deckEntries", r => new DeckEntrySpec(r.Name, r.ReadString("deckId"), r.ReadOptionalRelationKey("cardId", "cards"), r.ReadInt("count"), r.ReadInt("order"))).Rows,
                scenarios: source.ReadTable("scenarios", r => new ScenarioSpec(r.Name, r.ReadRelationKeys("actorIds", "actors"), r.ReadString("deckId"), r.ReadOptionalRelationKey("encounterId", "encounters"), r.ReadRelationKeys("companionIds", "companions"), r.ReadBool("isSolo"))).Rows);
        }
        private static T EnumValue<T>(string value) where T : struct, Enum
        {
            if (string.IsNullOrEmpty(value) || !Enum.IsDefined(typeof(T), value))
                throw new SpecValidationException($"Unknown {typeof(T).Name}: '{value}'. Use the exact symbolic name.");
            return (T)Enum.Parse(typeof(T), value);
        }
        private static IReadOnlyList<T> Enums<T>(IReadOnlyList<string> values) where T : struct, Enum
        {
            var result = new List<T>(values.Count);
            foreach (var value in values) result.Add(EnumValue<T>(value));
            return result.AsReadOnly();
        }
        private readonly struct RulesRow : ISpecRow
        {
            public RulesRow(string key, GameRulesSpec value) { Key = key; Value = value; }
            public string Key { get; }
            public GameRulesSpec Value { get; }
        }
        private readonly struct CatalogRow : ISpecRow
        {
            public CatalogRow(string key, int version) { Key = key; Version = version; }
            public string Key { get; }
            public int Version { get; }
        }
    }
}
