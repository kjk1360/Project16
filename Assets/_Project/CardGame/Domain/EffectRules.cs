using System;
using System.Collections.Generic;
using System.Linq;

namespace Project16.CardGame.Domain
{
    public sealed partial class GameRulesDomain
    {
        private sealed partial class Resolution
        {
            private int _effectChanges;
            private List<DamageIntent> _damageBatch;
            private sealed class DamageIntent
            {
                public string TargetId;
                public int Amount;
                public Context Context;
            }

            private IEnumerable<string> CardGroups(CardInstanceState card)
            {
                var group = _specs.Card(card.SpecId).EffectGroupId;
                if (!string.IsNullOrEmpty(group)) yield return group;
                foreach (var attached in card.AttachedEffectGroupIds) yield return attached;
            }

            private void RunCardEffects(CardInstanceState card, Context context)
            {
                var before = _effectChanges;
                foreach (var group in CardGroups(card)) RunGroup(group, context);
                if (_specs.Card(card.SpecId).Kind == CardKind.Item && before == _effectChanges)
                    throw new RuleRejected("The item has no applicable effect on the chosen targets.");
            }

            private void RunGroup(string groupId, Context context, GameEventKind trigger = GameEventKind.None)
            {
                if (string.IsNullOrEmpty(groupId)) return;
                foreach (var effect in _specs.Effects(groupId))
                {
                    Step();
                    if (effect.Trigger != trigger) continue;
                    foreach (var target in Targets(effect.TargetId, context))
                    {
                        if (!Condition(effect.ConditionId, target, context)) continue;
                        var value = string.IsNullOrEmpty(effect.ValueId) ? effect.Amount : Value(effect.ValueId, target, context);
                        var nested = context.Copy();
                        nested.TargetId = target;
                        switch (effect.Operation)
                        {
                            case EffectOperation.Damage: DealDamage(target, value, nested); break;
                            case EffectOperation.Heal: Heal(target, value, nested); break;
                            case EffectOperation.GainResource:
                                var player = Player(target);
                                var current = Resource(player, effect.ResourceKey);
                                var next = checked(current + value);
                                Require(next >= 0, "A resource effect would create a negative balance.");
                                if (next != current) { player.Resources[effect.ResourceKey] = next; _effectChanges++; }
                                break;
                            case EffectOperation.Draw:
                                var before = Player(target).Hand.Count;
                                Draw(Player(target), value);
                                if (Player(target).Hand.Count != before) _effectChanges++;
                                break;
                            case EffectOperation.MoveCard:
                                var moving = Card(target);
                                if (moving.Zone != effect.Destination) { MoveCard(moving, effect.Destination); _effectChanges++; }
                                break;
                            case EffectOperation.ApplyModifier: ApplyModifier(target, effect.ReferenceId, nested); break;
                            case EffectOperation.RemoveModifier: RemoveModifiers(target, modifier => modifier.SpecId == effect.ReferenceId); break;
                            case EffectOperation.RemoveModifiersByTag:
                                RemoveModifiers(target, modifier => _specs.Modifier(modifier.SpecId).Tags.Contains(effect.ReferenceId));
                                break;
                            case EffectOperation.ExtraAttack:
                                Require(InBattle && value >= 0, "Additional attacks require a live battle and a nonnegative amount.");
                                State.Battle.ExtraAttacks = checked(State.Battle.ExtraAttacks + value);
                                if (value > 0) _effectChanges++;
                                break;
                            case EffectOperation.CancelResponse:
                                Require(InBattle, "There is no battle response to cancel.");
                                State.Battle.PendingResponsePlayerId = null;
                                Increment("cancel-response:" + State.TurnSerial);
                                _effectChanges++;
                                break;
                            case EffectOperation.RunGroup: RunGroup(effect.ReferenceId, nested); break;
                            case EffectOperation.AddCard:
                                Require(value > 0, "AddCard requires a positive count.");
                                for (var i = 0; i < value; i++) AddCard(target, effect.ReferenceId, effect.Destination);
                                _effectChanges++;
                                break;
                            case EffectOperation.AttachUpgrade: AttachUpgrade(Card(target), effect.ReferenceId, nested); _effectChanges++; break;
                            default: throw new RuleRejected("Unsupported effect operation: " + effect.Operation);
                        }
                        if (State.Defeated) return;
                    }
                }
            }

            private List<string> Targets(string selectorId, Context context)
            {
                var selector = _specs.Selector(selectorId);
                var found = new List<string>();
                foreach (var kind in selector.Targets)
                {
                    switch (kind)
                    {
                        case TargetKind.Self: AddTarget(found, context.SelfId ?? context.OwnerId); break;
                        case TargetKind.SourceOwner: AddTarget(found, context.OwnerId); break;
                        case TargetKind.CurrentPlayer: AddTarget(found, State.CurrentPlayerId); break;
                        case TargetKind.NextPlayer: AddTarget(found, AdjacentPlayer(State.CurrentPlayerId, 1)); break;
                        case TargetKind.PreviousPlayer: AddTarget(found, AdjacentPlayer(State.CurrentPlayerId, -1)); break;
                        case TargetKind.AllPlayers: found.AddRange(State.PlayerOrder.Where(id => Player(id).Hp > 0)); break;
                        case TargetKind.BattleParticipants:
                            Require(State.Battle != null, "This selector needs a battle.");
                            found.AddRange(State.Battle.ParticipantIds.Where(id => Player(id).Hp > 0 && !Player(id).Escaped));
                            break;
                        case TargetKind.AllMonsters:
                            Require(State.Battle != null, "This selector needs monsters.");
                            found.AddRange(State.Battle.MonsterOrder.Where(id => State.Battle.Monsters[id].Hp > 0));
                            break;
                        case TargetKind.SourceCard: AddTarget(found, context.SourceId); break;
                        case TargetKind.SelectedPlayer:
                            found.Add(SelectOne(selectorId, FilterTargets(State.PlayerOrder.Where(id => Player(id).Hp > 0), selector), "Choose a player for " + selectorId + "."));
                            break;
                        case TargetKind.SelectedMonster:
                            found.Add(SelectOne(selectorId, FilterTargets(LivingMonsters(), selector), "Choose a monster for " + selectorId + "."));
                            break;
                        case TargetKind.SelectedCard:
                            found.Add(SelectOne(selectorId, FilterTargets(State.Cards.Values.Where(card => card.Zone != CardZone.Removed).OrderBy(card => card.Id, StringComparer.Ordinal).Select(card => card.Id), selector), "Choose a card for " + selectorId + "."));
                            break;
                        default: throw new RuleRejected("Unsupported target kind: " + kind);
                    }
                }
                found = FilterTargets(found, selector);
                return selector.DeduplicateTargets ? found.Distinct(StringComparer.Ordinal).ToList() : found;
            }

            private static void AddTarget(List<string> targets, string id)
            {
                Require(!string.IsNullOrEmpty(id), "The effect context does not contain its required target.");
                targets.Add(id);
            }

            private List<string> FilterTargets(IEnumerable<string> ids, TargetSelectorSpec selector) =>
                ids.Where(id => (string.IsNullOrEmpty(selector.RequiredTag) || HasTag(id, selector.RequiredTag)) &&
                    (string.IsNullOrEmpty(selector.Team) || Team(id) == selector.Team)).ToList();

            private string SelectOne(string key, IReadOnlyList<string> options, string reason)
            {
                Require(options.Count > 0, "No legal target exists for " + key + ".");
                if (!_command.Selections.TryGetValue(key, out var chosen))
                    throw new ChoiceRequired(new ChoiceRequest(key, reason, options));
                Require(chosen.Length == 1 && options.Contains(chosen[0]), "The submitted choice is not legal for " + key + ".");
                return chosen[0];
            }

            private string AdjacentPlayer(string current, int direction)
            {
                var ids = InBattle ? State.Battle.ParticipantIds.Where(id => !Player(id).Escaped && Player(id).Hp > 0).ToList() : State.PlayerOrder;
                Require(ids.Count > 0, "No adjacent player exists.");
                var index = ids.IndexOf(current);
                Require(index >= 0, "The current actor is absent from the target order.");
                return ids[(index + direction + ids.Count) % ids.Count];
            }

            private int Value(string valueId, string target, Context context)
            {
                Step();
                var definition = _specs.Value(valueId);
                int value;
                switch (definition.Kind)
                {
                    case ValueKind.Constant: value = definition.Amount; break;
                    case ValueKind.PlayerBaseAttack: value = Player(EntityOwner(context.OwnerId)).BaseAttack; break;
                    case ValueKind.SourceAttack:
                        if (context.SourceId != null && State.Cards.TryGetValue(context.SourceId, out var card)) value = CardAttack(card, context);
                        else if (context.SourceId != null && State.Battle.Monsters.TryGetValue(context.SourceId, out var monster)) value = Stat(monster.Id, StatKind.Attack, _specs.Monster(monster.SpecId).Attack, context);
                        else value = Player(context.OwnerId).BaseAttack;
                        break;
                    case ValueKind.TargetHp: value = Hp(target); break;
                    case ValueKind.TargetMissingHp: value = checked(MaxHp(target) - Hp(target)); break;
                    case ValueKind.TargetMaxHp: value = MaxHp(target); break;
                    case ValueKind.SourceResource: value = Resource(Player(context.OwnerId), definition.ReferenceId); break;
                    case ValueKind.TargetResource: value = Resource(Player(target), definition.ReferenceId); break;
                    case ValueKind.PlayerCount: value = State.Players.Count; break;
                    case ValueKind.BattleParticipantCount: value = State.Battle.ParticipantIds.Count(id => !Player(id).Escaped); break;
                    case ValueKind.Counter: value = Counter(definition.ReferenceId); break;
                    default: throw new RuleRejected("Unsupported value formula.");
                }
                Require(definition.Divisor > 0, "Value divisors must be positive.");
                return checked(value * definition.Multiplier) / definition.Divisor;
            }

            private bool Condition(string conditionId, string target, Context context)
            {
                if (string.IsNullOrEmpty(conditionId)) return true;
                Step();
                var condition = _specs.Condition(conditionId);
                switch (condition.Kind)
                {
                    case ConditionKind.Always: return true;
                    case ConditionKind.All: return condition.Children.All(child => Condition(child, target, context));
                    case ConditionKind.Any: return condition.Children.Any(child => Condition(child, target, context));
                    case ConditionKind.Not: return !Condition(condition.Children.Single(), target, context);
                    case ConditionKind.HasTag: return HasTag(target, condition.ReferenceId);
                    case ConditionKind.MissingTag: return !HasTag(target, condition.ReferenceId);
                    case ConditionKind.HpBelow: return Hp(target) < condition.Amount;
                    case ConditionKind.HpAtMost: return Hp(target) <= condition.Amount;
                    case ConditionKind.HpAtLeast: return Hp(target) >= condition.Amount;
                    case ConditionKind.ResourceAtLeast: return Resource(Player(target), condition.ReferenceId) >= condition.Amount;
                    case ConditionKind.PhaseIs: return State.PhaseId == condition.ReferenceId;
                    case ConditionKind.EventIs: return context.Event == condition.EventKind;
                    case ConditionKind.CounterAtLeast: return Counter(condition.ReferenceId) >= condition.Amount;
                    default: throw new RuleRejected("Unsupported effect condition.");
                }
            }

            private bool HasTag(string id, string tag)
            {
                if (id == null) return false;
                if (State.Players.TryGetValue(id, out var player) && player.Tags.Contains(tag)) return true;
                if (State.Battle.Monsters.TryGetValue(id, out var monster) && monster.Tags.Contains(tag)) return true;
                if (State.Cards.TryGetValue(id, out var card))
                {
                    if (_specs.Card(card.SpecId).Tags.Contains(tag)) return true;
                    if (!string.IsNullOrEmpty(card.CompanionId) && _specs.Companion(card.CompanionId).Tags.Contains(tag)) return true;
                }
                return State.Modifiers.Any(modifier => modifier.TargetId == id && _specs.Modifier(modifier.SpecId).Tags.Contains(tag));
            }

            private string Team(string id)
            {
                if (State.Players.TryGetValue(id, out var player)) return player.Team;
                if (State.Cards.TryGetValue(id, out var card)) return Player(card.OwnerId).Team;
                return State.Battle.Monsters.ContainsKey(id) ? "monsters" : null;
            }

            private int Hp(string id)
            {
                if (State.Players.TryGetValue(id, out var player)) return player.Hp;
                Require(State.Battle.Monsters.ContainsKey(id), "The selected target has no HP: " + id);
                return State.Battle.Monsters[id].Hp;
            }

            private int MaxHp(string id)
            {
                if (State.Players.TryGetValue(id, out var player)) return player.MaxHp;
                Require(State.Battle.Monsters.ContainsKey(id), "The selected target has no maximum HP: " + id);
                return State.Battle.Monsters[id].MaxHp;
            }

            private void DealDamage(string target, int amount, Context context)
            {
                Require(amount >= 0, "Damage cannot be negative.");
                Require(Hp(target) > 0, "A defeated target cannot receive another damage effect.");
                if (_damageBatch != null)
                {
                    _damageBatch.Add(new DamageIntent { TargetId = target, Amount = amount, Context = context.Copy() });
                    return;
                }
                var before = context.Copy();
                before.TargetId = target;
                before.Event = GameEventKind.BeforeDamage;
                Emit(GameEventKind.BeforeDamage, before);
                var damage = Math.Max(0, Stat(target, StatKind.DamageTaken, amount, before));
                if (State.Players.TryGetValue(target, out var player))
                {
                    var previous = player.Hp;
                    player.Hp = Math.Max(0, player.Hp - damage);
                    if (player.Hp != previous) _effectChanges++;
                }
                else
                {
                    var monster = State.Battle.Monsters[target];
                    var previous = monster.Hp;
                    monster.Hp = Math.Max(0, monster.Hp - damage);
                    if (monster.Hp != previous) _effectChanges++;
                    if (previous > 0 && monster.Hp == 0)
                    {
                        if (State.Players.ContainsKey(context.ActorId ?? "")) State.Battle.LastHitPlayerByMonster[target] = context.ActorId;
                        Emit(GameEventKind.MonsterDefeated, before);
                    }
                }
                Emit(GameEventKind.AfterDamage, before, damage);
                CheckDefeat(before);
            }

            private void Heal(string target, int amount, Context context)
            {
                Require(amount >= 0, "Healing cannot be negative.");
                var previous = Hp(target);
                var healed = checked((int)Math.Min(MaxHp(target), (long)previous + amount));
                if (State.Players.TryGetValue(target, out var player)) player.Hp = healed;
                else State.Battle.Monsters[target].Hp = healed;
                if (healed != previous) _effectChanges++;
            }

            private void CheckDefeat(Context context)
            {
                if (_resolvingDefeat || State.Defeated) return;
                _resolvingDefeat = true;
                try
                {
                    foreach (var id in State.PlayerOrder)
                    {
                        if (Player(id).Hp > 0) continue;
                        var pending = context.Copy();
                        pending.TargetId = id;
                        Emit(GameEventKind.BeforeDefeat, pending);
                        if (Player(id).Hp > 0) continue;
                        State.Defeated = true;
                        State.Victorious = false;
                        State.OutcomeReason = "A participant was defeated: " + id;
                        if (State.Battle != null) { State.Battle.Completed = true; State.Battle.Step = BattleStep.Completed; }
                        Emit(GameEventKind.Defeated, pending);
                        return;
                    }
                }
                finally { _resolvingDefeat = false; }
            }

            private void Draw(PlayerState player, int count)
            {
                Require(count >= 0, "Draw count cannot be negative.");
                for (var i = 0; i < count; i++)
                {
                    Step();
                    if (player.Deck.Count == 0)
                    {
                        if (player.Discard.Count == 0) break;
                        foreach (var id in player.Discard.ToArray()) MoveCard(Card(id), CardZone.Deck);
                        Shuffle(player.Deck);
                    }
                    MoveCard(Card(player.Deck[0]), CardZone.Hand);
                }
            }

            private void Shuffle(List<string> cards)
            {
                for (var i = cards.Count - 1; i > 0; i--)
                {
                    Step();
                    var index = (int)(NextRandom() % (ulong)(i + 1));
                    (cards[i], cards[index]) = (cards[index], cards[i]);
                }
            }

            private ulong NextRandom()
            {
                var value = State.RandomState == 0 ? 1UL : State.RandomState;
                value ^= value << 13; value ^= value >> 7; value ^= value << 17;
                State.RandomState = value;
                return value;
            }

            private void AddCard(string ownerId, string cardSpecId, CardZone zone)
            {
                Step();
                var owner = Player(ownerId);
                var definition = _specs.Card(cardSpecId);
                Require(string.IsNullOrEmpty(definition.CompanionId) || owner.CompanionIds.Contains(definition.CompanionId), "The skill's companion is not in the actor's roster.");
                var card = new CardInstanceState { Id = NewId("card"), SpecId = cardSpecId, OwnerId = ownerId, CompanionId = definition.CompanionId, Zone = zone };
                State.Cards.Add(card.Id, card);
                Zone(owner, zone).Add(card.Id);
            }

            private void MoveCard(CardInstanceState card, CardZone destination)
            {
                var owner = Player(card.OwnerId);
                Require(Zone(owner, card.Zone).Remove(card.Id), "Card zone ownership is inconsistent.");
                card.Zone = destination;
                if (destination != CardZone.Equipped) card.EquippedTo = null;
                Zone(owner, destination).Add(card.Id);
                Log(RuleEvents.CardMoved, owner.Id, card.Id, amount: (int)destination);
            }

            private static List<string> Zone(PlayerState player, CardZone zone)
            {
                switch (zone)
                {
                    case CardZone.Deck: return player.Deck;
                    case CardZone.Hand: return player.Hand;
                    case CardZone.Discard: return player.Discard;
                    case CardZone.Removed: return player.Removed;
                    case CardZone.Equipped: return player.Equipped;
                    default: throw new RuleRejected("Unknown card zone.");
                }
            }

            private static int Resource(PlayerState player, string key)
            {
                Require(!string.IsNullOrEmpty(key), "A resource key is required.");
                return player.Resources.TryGetValue(key, out var value) ? value : 0;
            }

            private void PayResource(PlayerState player, string key, int amount)
            {
                Require(amount >= 0, "A card cost cannot be negative.");
                if (amount == 0) return;
                var current = Resource(player, key);
                Require(current >= amount, "Insufficient " + key + ".");
                player.Resources[key] = current - amount;
            }

            private int Counter(string key) => key != null && State.Counters.TryGetValue(key, out var value) ? value : 0;
            private void Increment(string key) => State.Counters[key] = checked(Counter(key) + 1);
        }
    }
}
