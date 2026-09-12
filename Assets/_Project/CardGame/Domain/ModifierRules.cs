using System;
using System.Linq;

namespace Project16.CardGame.Domain
{
    public sealed partial class GameRulesDomain
    {
        private sealed partial class Resolution
        {
            private void ApplyModifier(string targetId, string modifierId, Context context)
            {
                Require(State.Players.ContainsKey(targetId) || State.Cards.ContainsKey(targetId) || State.Battle.Monsters.ContainsKey(targetId), "Unknown modifier target.");
                var definition = _specs.Modifier(modifierId);
                var duration = _specs.Duration(definition.DurationId);
                var matches = State.Modifiers.Where(value => value.TargetId == targetId && value.SpecId == modifierId).ToArray();
                ModifierState modifier = null;
                if (matches.Length > 0)
                {
                    switch (definition.StackPolicy)
                    {
                        case StackPolicy.Reject: throw new RuleRejected("This modifier does not permit another application: " + modifierId);
                        case StackPolicy.Replace:
                            foreach (var existing in matches) State.Modifiers.Remove(existing);
                            break;
                        case StackPolicy.Refresh: modifier = matches[0]; break;
                        case StackPolicy.Add:
                            modifier = matches[0];
                            modifier.Stacks = Math.Min(definition.MaxStacks, checked(modifier.Stacks + 1));
                            break;
                    }
                }
                if (modifier == null)
                {
                    modifier = new ModifierState { Id = NewId("modifier"), SpecId = modifierId, TargetId = targetId };
                    State.Modifiers.Add(modifier);
                }
                modifier.OwnerId = context.OwnerId;
                modifier.SourceId = context.SourceId;
                modifier.AppliedRound = State.Round;
                modifier.AppliedTurnSerial = State.TurnSerial;
                modifier.AppliedPhaseSerial = State.PhaseSerial;
                modifier.RemainingOccurrences = duration.Kind == DurationKind.Permanent ? 0 : duration.Occurrences;
                modifier.AnchorPlayerId = duration.Anchor == DurationAnchor.CurrentPlayer ? State.CurrentPlayerId :
                    duration.Anchor == DurationAnchor.SourceOwner ? EntityOwner(context.OwnerId) : EntityOwner(targetId);
                if (duration.Kind == DurationKind.OwnerTurnStart || duration.Kind == DurationKind.OwnerTurnEnd)
                    Require(State.Players.ContainsKey(modifier.AnchorPlayerId ?? ""), "Personal-turn duration needs a player anchor; use SourceOwner for a monster status cast by a player.");
                RefreshMaxHp(targetId);
                _effectChanges++;
                Log(RuleEvents.ModifierApplied, context.ActorId, targetId, modifier.Id, modifier.Stacks);
            }

            private void RemoveModifiers(string targetId, Func<ModifierState, bool> match)
            {
                foreach (var modifier in State.Modifiers.Where(value => value.TargetId == targetId && match(value)).ToArray())
                {
                    State.Modifiers.Remove(modifier);
                    _effectChanges++;
                    Log(RuleEvents.ModifierRemoved, State.CurrentPlayerId, targetId, modifier.Id);
                }
                RefreshMaxHp(targetId);
            }

            private string EntityOwner(string id)
            {
                if (id != null && State.Cards.TryGetValue(id, out var card)) return card.OwnerId;
                return id;
            }

            private int Stat(string targetId, StatKind stat, int baseValue, Context context)
            {
                var value = baseValue;
                foreach (var modifier in State.Modifiers.Where(item => item.TargetId == targetId)
                             .OrderBy(item => _specs.Modifier(item.SpecId).Priority))
                {
                    var definition = _specs.Modifier(modifier.SpecId);
                    if (definition.Kind != ModifierKind.Stat || definition.Stat != stat || !Condition(definition.ConditionId, targetId, context)) continue;
                    for (var i = 0; i < modifier.Stacks; i++) value = ApplyStat(value, definition.Operation, definition.Amount);
                }
                return value;
            }

            private static int ApplyStat(int value, StatOperation operation, int amount)
            {
                switch (operation)
                {
                    case StatOperation.Add: return checked(value + amount);
                    case StatOperation.MultiplyPercent: return checked((int)((long)value * amount / 100));
                    case StatOperation.Set: return amount;
                    case StatOperation.Minimum: return Math.Max(value, amount);
                    case StatOperation.Maximum: return Math.Min(value, amount);
                    default: throw new RuleRejected("Unsupported modifier operation.");
                }
            }

            private void RefreshMaxHp(string targetId)
            {
                var context = ActorContext(EntityOwner(targetId));
                if (State.Players.TryGetValue(targetId, out var player))
                {
                    var max = Stat(targetId, StatKind.MaxHp, _specs.Actor(player.ActorSpecId).MaxHp, context);
                    Require(max > 0, "Maximum HP modifiers must leave a positive maximum.");
                    player.MaxHp = max;
                    player.Hp = Math.Min(player.Hp, max);
                }
                else if (State.Battle.Monsters.TryGetValue(targetId, out var monster))
                {
                    var definition = _specs.Monster(monster.SpecId);
                    var baseHp = string.IsNullOrEmpty(definition.MaxHpValueId) ? definition.MaxHp : Value(definition.MaxHpValueId, targetId, context);
                    var max = Stat(targetId, StatKind.MaxHp, baseHp, context);
                    Require(max > 0, "Maximum HP modifiers must leave a positive maximum.");
                    monster.MaxHp = max;
                    monster.Hp = Math.Min(monster.Hp, max);
                }
            }

            private void CheckAction(string actorId, ActionKind action, string sourceId)
            {
                Require(!IsActionBlocked(actorId, action, sourceId), "An active modifier prevents " + action + ".");
            }

            private bool IsActionBlocked(string actorId, ActionKind action, string sourceId)
            {
                var context = ActorContext(actorId, sourceId);
                foreach (var modifier in State.Modifiers)
                {
                    if (modifier.TargetId != actorId && modifier.TargetId != sourceId) continue;
                    var definition = _specs.Modifier(modifier.SpecId);
                    if (definition.Kind == ModifierKind.PreventAction && definition.BlockedAction == action &&
                        Condition(definition.ConditionId, modifier.TargetId, context)) return true;
                }
                if (sourceId != null && State.Cards.TryGetValue(sourceId, out var card))
                    foreach (var modifierId in card.AttachedModifierIds)
                    {
                        var definition = _specs.Modifier(modifierId);
                        if (definition.Kind == ModifierKind.PreventAction && definition.BlockedAction == action && Condition(definition.ConditionId, card.Id, context)) return true;
                    }
                return false;
            }

            private void Emit(GameEventKind kind, Context original, int amount = 0)
            {
                Step();
                var context = original.Copy();
                context.Event = kind;
                Increment(kind.ToString());
                if (context.ActorId != null) Increment(kind + ":" + context.ActorId);
                Log(EventLabel(kind), context.ActorId, context.TargetId, context.SourceId, amount);
                var modifiers = State.Modifiers.ToArray();
                if (kind == GameEventKind.TurnStart || kind == GameEventKind.PhaseStart)
                    ExpireModifiers(modifiers, kind, context);
                foreach (var modifier in modifiers)
                {
                    if (!State.Modifiers.Contains(modifier)) continue;
                    var definition = _specs.Modifier(modifier.SpecId);
                    if (definition.Kind != ModifierKind.PeriodicTrigger || definition.Trigger != kind || !EventApplies(modifier, kind, context)) continue;
                    if (!Condition(definition.ConditionId, modifier.TargetId, context)) continue;
                    if ((kind == GameEventKind.TurnStart || kind == GameEventKind.TurnEnd) && modifier.LastTriggerTurnSerial == State.TurnSerial) continue;
                    if ((kind == GameEventKind.PhaseStart || kind == GameEventKind.PhaseEnd) && modifier.LastTriggerPhaseSerial == State.PhaseSerial) continue;
                    modifier.TriggerOccurrences = checked(modifier.TriggerOccurrences + 1);
                    if (kind == GameEventKind.TurnStart || kind == GameEventKind.TurnEnd) modifier.LastTriggerTurnSerial = State.TurnSerial;
                    if (kind == GameEventKind.PhaseStart || kind == GameEventKind.PhaseEnd) modifier.LastTriggerPhaseSerial = State.PhaseSerial;
                    Require(definition.EveryOccurrences > 0, "Periodic effects need a positive occurrence interval.");
                    if (modifier.TriggerOccurrences % definition.EveryOccurrences != 0) continue;
                    var triggered = context.Copy();
                    triggered.OwnerId = modifier.OwnerId;
                    triggered.SourceId = modifier.SourceId;
                    triggered.SelfId = modifier.TargetId;
                    triggered.TargetId = modifier.TargetId;
                    RunGroup(definition.EffectGroupId, triggered);
                }
                // Passive card effects observe their owner's actor/recipient events while in an active
                // authored zone. Global phase/battle events are visible to all such sources.
                foreach (var card in State.Cards.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray())
                {
                    if (card.Zone != CardZone.Hand && card.Zone != CardZone.Equipped && card.Id != context.SourceId) continue;
                    if (!IsGlobalEvent(kind) && card.OwnerId != context.ActorId && card.OwnerId != context.TargetId) continue;
                    var triggered = context.Copy();
                    triggered.OwnerId = card.OwnerId;
                    triggered.SourceId = card.Id;
                    triggered.SelfId = card.OwnerId;
                    foreach (var group in CardGroups(card)) RunGroup(group, triggered, kind);
                }
                if (kind != GameEventKind.TurnStart && kind != GameEventKind.PhaseStart)
                    ExpireModifiers(modifiers, kind, context);
            }

            private void ExpireModifiers(ModifierState[] modifiers, GameEventKind kind, Context context)
            {
                foreach (var modifier in modifiers)
                {
                    if (!State.Modifiers.Contains(modifier)) continue;
                    var duration = _specs.Duration(_specs.Modifier(modifier.SpecId).DurationId);
                    if (!DurationBoundary(modifier, duration, kind, context)) continue;
                    modifier.RemainingOccurrences = Math.Max(0, modifier.RemainingOccurrences - 1);
                    if (modifier.RemainingOccurrences > 0) continue;
                    State.Modifiers.Remove(modifier);
                    RefreshMaxHp(modifier.TargetId);
                    Log(RuleEvents.ModifierRemoved, context.ActorId, modifier.TargetId, modifier.Id);
                }
            }

            private static bool IsGlobalEvent(GameEventKind kind) => kind == GameEventKind.PhaseStart || kind == GameEventKind.PhaseEnd ||
                kind == GameEventKind.RoundEnd || kind == GameEventKind.BattleStart || kind == GameEventKind.BattleEnd;

            private static bool EventApplies(ModifierState modifier, GameEventKind kind, Context context)
            {
                if (IsGlobalEvent(kind)) return true;
                if (kind == GameEventKind.TurnStart || kind == GameEventKind.TurnEnd)
                    return modifier.AnchorPlayerId == context.ActorId;
                if (kind == GameEventKind.BeforeDamage || kind == GameEventKind.AfterDamage || kind == GameEventKind.BeforeDefeat || kind == GameEventKind.Defeated)
                    return modifier.TargetId == context.TargetId;
                return modifier.TargetId == context.ActorId || modifier.TargetId == context.SourceId;
            }

            private bool DurationBoundary(ModifierState modifier, DurationSpec duration, GameEventKind kind, Context context)
            {
                switch (duration.Kind)
                {
                    case DurationKind.Permanent: return false;
                    case DurationKind.CurrentTurn: return kind == GameEventKind.TurnEnd && State.TurnSerial >= modifier.AppliedTurnSerial;
                    case DurationKind.OwnerTurnStart: return kind == GameEventKind.TurnStart && context.ActorId == modifier.AnchorPlayerId && State.TurnSerial > modifier.AppliedTurnSerial;
                    case DurationKind.OwnerTurnEnd: return kind == GameEventKind.TurnEnd && context.ActorId == modifier.AnchorPlayerId && State.TurnSerial > modifier.AppliedTurnSerial;
                    case DurationKind.GlobalPhaseStart: return kind == GameEventKind.PhaseStart && State.PhaseId == duration.PhaseId && State.PhaseSerial > modifier.AppliedPhaseSerial;
                    case DurationKind.GlobalPhaseEnd: return kind == GameEventKind.PhaseEnd && State.PhaseId == duration.PhaseId;
                    case DurationKind.BattleEnd: return kind == GameEventKind.BattleEnd;
                    case DurationKind.RoundEnd: return kind == GameEventKind.RoundEnd;
                    default: throw new RuleRejected("Unsupported modifier duration.");
                }
            }

            private static string EventLabel(GameEventKind kind)
            {
                switch (kind)
                {
                    case GameEventKind.TurnStart: return RuleEvents.TurnStarted;
                    case GameEventKind.TurnEnd: return RuleEvents.TurnEnded;
                    case GameEventKind.PhaseStart: return RuleEvents.PhaseStarted;
                    case GameEventKind.PhaseEnd: return RuleEvents.PhaseEnded;
                    case GameEventKind.RoundEnd: return RuleEvents.RoundEnded;
                    case GameEventKind.CardUsed: return RuleEvents.CardUsed;
                    case GameEventKind.BeforeAttack: return RuleEvents.BeforeAttack;
                    case GameEventKind.AfterAttack: return RuleEvents.AfterAttack;
                    case GameEventKind.BeforeDamage: return RuleEvents.BeforeDamage;
                    case GameEventKind.AfterDamage: return RuleEvents.AfterDamage;
                    case GameEventKind.BeforeDefeat: return RuleEvents.BeforeDefeat;
                    case GameEventKind.SwitchSucceeded: return RuleEvents.SwitchSucceeded;
                    case GameEventKind.MonsterDefeated: return RuleEvents.MonsterDefeated;
                    case GameEventKind.BattleStart: return RuleEvents.BattleStarted;
                    case GameEventKind.BattleEnd: return RuleEvents.BattleEnded;
                    default: return kind.ToString();
                }
            }
        }
    }
}
