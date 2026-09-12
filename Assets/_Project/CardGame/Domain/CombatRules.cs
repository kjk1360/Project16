using System;
using System.Collections.Generic;
using System.Linq;

namespace Project16.CardGame.Domain
{
    public sealed partial class GameRulesDomain
    {
        private sealed partial class Resolution
        {
            private void StartBattle(string encounterId, IEnumerable<string> participantIds)
            {
                Require(!InBattle, "A battle is already active.");
                var encounter = _specs.Encounter(encounterId);
                var requested = participantIds == null ? new List<string>() : new List<string>(participantIds);
                if (requested.Count == 0) requested.AddRange(State.PlayerOrder);
                Require(requested.Distinct(StringComparer.Ordinal).Count() == requested.Count, "A player may enter an encounter only once.");
                foreach (var playerId in requested) Require(Player(playerId).Hp > 0, "A defeated player cannot join a battle.");
                var participantSet = new HashSet<string>(requested, StringComparer.Ordinal);
                var ordered = new List<string>();
                var leader = State.PlayerOrder.IndexOf(State.LeaderPlayerId);
                for (var i = 0; i < State.PlayerOrder.Count; i++)
                {
                    var playerId = State.PlayerOrder[(Math.Max(0, leader) + i) % State.PlayerOrder.Count];
                    if (participantSet.Contains(playerId)) ordered.Add(playerId);
                }
                Require(ordered.Count > 0, "An encounter requires participants.");
                var previousPhase = _specs.Phase(State.PhaseId);
                if (previousPhase.Kind == PhaseKind.Exploration)
                    foreach (var playerId in ordered)
                    {
                        var key = "exploration-action:" + State.Round + ":" + playerId;
                        Require(Counter(key) == 0, "A participant already used their exploration action this round.");
                        Increment(key);
                    }
                var battle = new BattleState { EncounterId = encounterId, Step = BattleStep.PlayerAction };
                battle.ParticipantIds.AddRange(ordered);
                State.Battle = battle;
                foreach (var monsterId in encounter.MonsterIds)
                {
                    Step();
                    var definition = _specs.Monster(monsterId);
                    var authoredHp = string.IsNullOrEmpty(definition.MaxHpValueId) ? definition.MaxHp :
                        Value(definition.MaxHpValueId, null, ActorContext(ordered[0]));
                    Require(authoredHp > 0, "Monster maximum HP must be authored and positive.");
                    var id = NewId("monster");
                    var monster = new MonsterState { Id = id, SpecId = definition.Key, Hp = authoredHp, MaxHp = authoredHp };
                    monster.Tags.AddRange(definition.Tags);
                    battle.Monsters.Add(id, monster);
                    battle.MonsterOrder.Add(id);
                }
                foreach (var id in ordered) Player(id).Escaped = false;
                State.CurrentPlayerId = ordered[0];
                if (!string.IsNullOrEmpty(_specs.Rules.CombatPhaseId) && State.PhaseId != _specs.Rules.CombatPhaseId)
                {
                    RunGroup(previousPhase.ExitGroupId, ActorContext(State.CurrentPlayerId));
                    Emit(GameEventKind.PhaseEnd, ActorContext(State.CurrentPlayerId));
                    EnterPhase(_specs.Rules.CombatPhaseId);
                }
                var context = ActorContext(State.CurrentPlayerId);
                Emit(GameEventKind.BattleStart, context);
                RunGroup(encounter.EnterGroupId, context);
                CheckDefeat(context);
                if (State.Defeated) return;
                if (CheckBattleClear(context)) return;
                BeginPersonalTurn(State.CurrentPlayerId);
            }

            private void PlayCards(bool switching)
            {
                Require(_command.SourceIds.Count > 0, "Choose at least one card.");
                Require(_command.SourceIds.Distinct(StringComparer.Ordinal).Count() == _command.SourceIds.Count, "The same card cannot be played twice in one action.");
                var actor = Player(_command.ActorId);
                var sources = _command.SourceIds.Select(Card).ToArray();
                foreach (var card in sources)
                {
                    Require(card.OwnerId == actor.Id && card.Zone == CardZone.Hand, "Played cards must be in the actor's hand.");
                    var phase = InBattle ? PhaseKind.Combat : _specs.Phase(State.PhaseId).Kind;
                    Require(_specs.Card(card.SpecId).AllowedPhases.Contains(phase), "This card cannot be used in the current phase.");
                }
                if (!InBattle)
                {
                    Require(!switching, "Switch requires a live battle.");
                    PlayOutsideBattle(actor, sources);
                    return;
                }
                Require(!actor.Escaped && actor.Hp > 0 && State.Battle.ParticipantIds.Contains(actor.Id), "The actor is not a live battle participant.");
                var emergencyOnly = sources.All(card => _specs.Card(card.SpecId).IsEmergency);
                if (!switching && emergencyOnly)
                {
                    Require(State.Battle.Step == BattleStep.PlayerAction || State.Battle.Step == BattleStep.SwitchWindow || State.Battle.Step == BattleStep.MonsterResponse,
                        "There is no emergency use window.");
                    PlayEmergency(actor, sources);
                    return;
                }
                if (switching)
                {
                    Require(State.Battle.Step == BattleStep.SwitchWindow, "There is no switch window.");
                    Require(actor.Id == NextParticipant(State.CurrentPlayerId), "Only the next battle participant may switch.");
                    Require(!State.Battle.PendingEscape || actor.Id != State.CurrentPlayerId, "A fleeing solo actor cannot also replace their own action with a switch.");
                    CheckAction(actor.Id, ActionKind.Switch, null);
                    var switchCard = _command.SourceIds.Select(Card).FirstOrDefault(card => _specs.Card(card.SpecId).Kind == CardKind.Skill);
                    Require(switchCard != null && _specs.Card(switchCard.SpecId).CanSwitch, "The action requires a skill with the Switch capability.");
                    CheckAction(actor.Id, ActionKind.Switch, switchCard.Id);
                    Emit(GameEventKind.SwitchAttempt, ActorContext(actor.Id, switchCard.Id));
                    var outgoing = State.CurrentPlayerId;
                    if (State.Battle.PendingEscape) FinishEscape(outgoing);
                    ClosePersonalTurn(outgoing);
                    if (State.Defeated) return;
                    BeginPersonalTurn(actor.Id);
                    Emit(GameEventKind.SwitchSucceeded, ActorContext(actor.Id, switchCard.Id));
                }
                else
                {
                    Require(State.Battle.Step == BattleStep.PlayerAction && actor.Id == State.CurrentPlayerId, "It is not this player's action window.");
                }
                CheckAction(actor.Id, ActionKind.PlayCard, null);
                var skills = sources.Count(card => _specs.Card(card.SpecId).Kind == CardKind.Skill && !_specs.Card(card.SpecId).IsEmergency);
                var items = sources.Count(card => _specs.Card(card.SpecId).Kind != CardKind.Skill && !_specs.Card(card.SpecId).IsEmergency);
                Require(actor.CardActionsUsed + skills <= Stat(actor.Id, StatKind.CardActions, _specs.Rules.CardActionsPerTurn, ActorContext(actor.Id)), "The skill action budget was exceeded.");
                Require(actor.ItemActionsUsed + items <= Stat(actor.Id, StatKind.ItemActions, _specs.Rules.ItemActionsPerTurn, ActorContext(actor.Id)), "The item/equipment action budget was exceeded.");
                actor.CardActionsUsed += skills;
                actor.ItemActionsUsed += items;
                State.Battle.ExtraAttacks = 0;
                State.Battle.ActiveCardId = sources.FirstOrDefault(card => _specs.Card(card.SpecId).Kind == CardKind.Skill)?.Id;
                foreach (var card in sources)
                {
                    var definition = _specs.Card(card.SpecId);
                    CheckAction(actor.Id, ActionKind.PlayCard, card.Id);
                    PayResource(actor, definition.ResourceKey, definition.Cost);
                    if (definition.Kind == CardKind.Equipment)
                    {
                        Require(State.Battle.ActiveCardId != null, "Hand equipment needs a skill played in the same action.");
                        CheckAction(actor.Id, ActionKind.Equip, card.Id);
                        card.EquippedTo = State.Battle.ActiveCardId;
                        MoveCard(card, CardZone.Equipped);
                    }
                    card.UsesThisTurn = checked(card.UsesThisTurn + 1);
                    var context = ActorContext(actor.Id, card.Id);
                    Emit(GameEventKind.CardUsed, context);
                    RunCardEffects(card, context);
                    if (State.Defeated) return;
                }
                foreach (var skill in sources.Where(card => _specs.Card(card.SpecId).Kind == CardKind.Skill))
                {
                    State.Battle.ActiveCardId = skill.Id;
                    PerformSkillAttacks(skill, ActorContext(actor.Id, skill.Id));
                    if (State.Defeated || !InBattle) break;
                }
                foreach (var card in sources)
                    if (card.Zone == CardZone.Hand)
                        MoveCard(card, _specs.Card(card.SpecId).Kind == CardKind.Item ? CardZone.Removed : CardZone.Discard);
                if (State.Defeated || !InBattle) return;
                if (!CheckBattleClear(ActorContext(actor.Id))) OpenSwitchWindow(actor.Id, false);
            }

            private void PerformSkillAttacks(CardInstanceState card, Context context)
            {
                // PreventAction(Attack) suppresses the native attack, not the skill's unrelated
                // effects. PreventAction(PlayCard) is the separate whole-operation restriction.
                if (IsActionBlocked(context.ActorId, ActionKind.Attack, card.Id)) return;
                var definition = _specs.Card(card.SpecId);
                var valueKind = string.IsNullOrEmpty(definition.AttackValueId) ? ValueKind.Constant : _specs.Value(definition.AttackValueId).Kind;
                var targetDependent = valueKind == ValueKind.TargetHp || valueKind == ValueKind.TargetMissingHp || valueKind == ValueKind.TargetMaxHp;
                var preparation = State.Modifiers.Any(modifier => _specs.Modifier(modifier.SpecId).Trigger == GameEventKind.BeforeAttack && EventApplies(modifier, GameEventKind.BeforeAttack, context)) ||
                    State.Cards.Values.Where(candidate => candidate.OwnerId == card.OwnerId &&
                        (candidate.Zone == CardZone.Hand || candidate.Zone == CardZone.Equipped || candidate.Id == card.Id))
                        .Any(candidate => CardGroups(candidate).Any(group => _specs.Effects(group).Any(effect => effect.Trigger == GameEventKind.BeforeAttack)));
                context.Event = GameEventKind.BeforeAttack;
                if (!targetDependent && !preparation && CardAttack(card, context) <= 0) return;
                var remaining = 1;
                do
                {
                    Step();
                    if (!InBattle || LivingMonsters().Count == 0) break;
                    var target = ChooseAttackTarget();
                    context.TargetId = target;
                    context.Event = GameEventKind.BeforeAttack;
                    Emit(GameEventKind.BeforeAttack, context);
                    var attack = CardAttack(card, context);
                    if (attack > 0) DealDamage(target, attack, context);
                    context.TargetId = target;
                    Emit(GameEventKind.AfterAttack, context);
                    remaining = checked(remaining - 1 + State.Battle.ExtraAttacks);
                    State.Battle.ExtraAttacks = 0;
                } while (remaining > 0 && !State.Defeated);
            }

            private int CardAttack(CardInstanceState card, Context context)
            {
                var definition = _specs.Card(card.SpecId);
                var value = string.IsNullOrEmpty(definition.AttackValueId) ? definition.Attack :
                    Value(definition.AttackValueId, context.TargetId, context);
                foreach (var upgradeId in card.UpgradeIds) value = checked(value + _specs.Upgrade(upgradeId).AttackBonus);
                foreach (var equipmentId in Player(card.OwnerId).Equipped)
                {
                    var equipment = Card(equipmentId);
                    if (string.IsNullOrEmpty(equipment.EquippedTo) || equipment.EquippedTo == card.Id)
                        value = checked(value + Stat(equipment.Id, StatKind.Attack, _specs.Card(equipment.SpecId).Attack, context));
                }
                value = Stat(card.Id, StatKind.Attack, value, context);
                value = Stat(card.OwnerId, StatKind.Attack, value, context);
                foreach (var modifierId in card.AttachedModifierIds)
                {
                    var modifier = _specs.Modifier(modifierId);
                    if (modifier.Kind == ModifierKind.Stat && modifier.Stat == StatKind.Attack && Condition(modifier.ConditionId, card.Id, context))
                        value = ApplyStat(value, modifier.Operation, modifier.Amount);
                }
                return Math.Max(0, value);
            }

            private void ActivateAbility()
            {
                Require(_command.SourceIds.Count == 1, "Choose one carried equipment source.");
                var card = Card(_command.SourceIds[0]);
                Require(card.OwnerId == _command.ActorId && card.Zone == CardZone.Equipped, "The ability source must be carried equipment.");
                Require(card.UsesThisTurn == 0, "This carried ability was already used during this personal turn.");
                var definition = _specs.Card(card.SpecId);
                Require(definition.Kind == CardKind.Equipment, "Only equipment exposes a carried ability.");
                var phase = InBattle ? PhaseKind.Combat : _specs.Phase(State.PhaseId).Kind;
                Require(definition.AllowedPhases.Contains(phase), "The carried ability cannot be used in this phase.");
                if (InBattle && !definition.IsEmergency)
                    Require(State.Battle.Step == BattleStep.PlayerAction && _command.ActorId == State.CurrentPlayerId,
                        "Non-emergency carried abilities require the owner's action window.");
                if (InBattle && definition.IsEmergency)
                    Require(State.Battle.ParticipantIds.Contains(card.OwnerId) && !Player(card.OwnerId).Escaped,
                        "Emergency equipment belongs to a current battle participant.");
                var groups = CardGroups(card).ToArray();
                Require(string.IsNullOrEmpty(_command.SpecId) || groups.Contains(_command.SpecId), "The requested effect group is not attached to this source.");
                CheckAction(card.OwnerId, ActionKind.PlayCard, card.Id);
                card.UsesThisTurn++;
                var context = ActorContext(card.OwnerId, card.Id);
                Emit(GameEventKind.CardUsed, context);
                if (string.IsNullOrEmpty(_command.SpecId)) RunCardEffects(card, context);
                else RunGroup(_command.SpecId, context);
                if (!State.Defeated && InBattle && !CheckBattleClear(context) && !definition.IsEmergency) OpenSwitchWindow(card.OwnerId, false);
            }

            private void Pass(bool flee)
            {
                Require(InBattle && State.Battle.Step == BattleStep.PlayerAction && _command.ActorId == State.CurrentPlayerId,
                    "Pass/flee requires the current player's action window.");
                CheckAction(_command.ActorId, flee ? ActionKind.Flee : ActionKind.Pass, null);
                OpenSwitchWindow(_command.ActorId, flee);
            }

            private void OpenSwitchWindow(string actorId, bool escape)
            {
                State.Battle.Step = BattleStep.SwitchWindow;
                State.Battle.PendingResponsePlayerId = Counter("cancel-response:" + State.TurnSerial) > 0 ? null : actorId;
                State.Battle.PendingEscape = escape;
                State.Battle.LastActingPlayerId = actorId;
            }

            private void DeclineSwitch()
            {
                Require(InBattle && State.Battle.Step == BattleStep.SwitchWindow, "There is no pending switch decision.");
                Require(_command.ActorId == NextParticipant(State.CurrentPlayerId), "Only the next participant may decline the switch.");
                Log(RuleEvents.SwitchDeclined, _command.ActorId);
                Emit(GameEventKind.SwitchFailed, ActorContext(_command.ActorId));
                ResolveMonsterResponse();
            }

            private void ResolveMonsterResponse()
            {
                var battle = State.Battle;
                var outgoing = State.CurrentPlayerId;
                battle.Step = BattleStep.MonsterResponse;
                var recipient = battle.PendingResponsePlayerId;
                // The surviving set is captured once. Each printed attack resolves in MonsterOrder;
                // this is one response batch, never an interleaved new player turn.
                var surviving = LivingMonsters();
                if (!string.IsNullOrEmpty(recipient))
                {
                    Log(RuleEvents.MonsterResponse, recipient);
                    _damageBatch = new System.Collections.Generic.List<DamageIntent>();
                    foreach (var monsterId in surviving)
                    {
                        Step();
                        if (State.Defeated) break;
                        if (IsActionBlocked(monsterId, ActionKind.Attack, monsterId)) continue;
                        var monster = battle.Monsters[monsterId];
                        var definition = _specs.Monster(monster.SpecId);
                        var context = new Context { ActorId = recipient, OwnerId = monsterId, SelfId = monsterId, SourceId = monsterId, TargetId = recipient };
                        Emit(GameEventKind.BeforeAttack, context);
                        if (string.IsNullOrEmpty(definition.AttackGroupId))
                            DealDamage(recipient, Math.Max(0, Stat(monsterId, StatKind.Attack, definition.Attack, context)), context);
                        else RunGroup(definition.AttackGroupId, context);
                        Emit(GameEventKind.AfterAttack, context);
                        if (battle.PendingResponsePlayerId == null) { _damageBatch.Clear(); break; }
                    }
                    var intents = _damageBatch;
                    _damageBatch = null;
                    foreach (var group in intents.GroupBy(intent => intent.TargetId))
                    {
                        var total = 0;
                        foreach (var intent in group) total = checked(total + intent.Amount);
                        var context = group.First().Context.Copy();
                        context.SourceId = null;
                        DealDamage(group.Key, total, context);
                        if (State.Defeated) break;
                    }
                }
                if (State.Defeated) return;
                if (battle.PendingEscape) FinishEscape(outgoing);
                ClosePersonalTurn(outgoing);
                if (State.Defeated) return;
                if (CheckBattleClear(ActorContext(outgoing))) return;
                var next = NextParticipant(outgoing);
                if (next == null) { FinishBattle(false, ActorContext(outgoing)); return; }
                BeginPersonalTurn(next);
            }

            private void BeginPersonalTurn(string playerId)
            {
                State.CurrentPlayerId = playerId;
                State.TurnSerial = checked(State.TurnSerial + 1);
                var player = Player(playerId);
                player.TurnCount = checked(player.TurnCount + 1);
                player.CardActionsUsed = 0;
                player.ItemActionsUsed = 0;
                foreach (var card in State.Cards.Values)
                {
                    card.UsesThisTurn = 0;
                    card.AbilityUseCounts.Clear();
                }
                State.Battle.ActivePlayerIndex = State.Battle.ParticipantIds.IndexOf(playerId);
                State.Battle.Step = BattleStep.PlayerAction;
                State.Battle.PendingResponsePlayerId = null;
                State.Battle.PendingEscape = false;
                State.Battle.ActiveCardId = null;
                State.Battle.ExtraAttacks = 0;
                Emit(GameEventKind.TurnStart, ActorContext(playerId));
            }

            private void ClosePersonalTurn(string playerId)
            {
                Emit(GameEventKind.TurnEnd, ActorContext(playerId));
                foreach (var equipmentId in Player(playerId).Equipped.ToArray())
                {
                    var equipment = Card(equipmentId);
                    if (!string.IsNullOrEmpty(equipment.EquippedTo))
                    {
                        equipment.EquippedTo = null;
                        MoveCard(equipment, CardZone.Discard);
                    }
                }
            }

            private void FinishEscape(string playerId)
            {
                Player(playerId).Escaped = true;
                Log(RuleEvents.PlayerEscaped, playerId);
            }

            private string NextParticipant(string current)
            {
                var ids = State.Battle.ParticipantIds;
                var index = ids.IndexOf(current);
                for (var i = 1; i <= ids.Count; i++)
                {
                    var id = ids[(Math.Max(0, index) + i) % ids.Count];
                    if (!Player(id).Escaped && Player(id).Hp > 0) return id;
                }
                return null;
            }

            private List<string> LivingMonsters() => State.Battle.MonsterOrder.Where(id => State.Battle.Monsters[id].Hp > 0).ToList();

            private int _attackSelectionIndex;

            private string ChooseAttackTarget()
            {
                var options = LivingMonsters();
                Require(options.Count > 0, "There is no living attack target.");
                var index = _attackSelectionIndex++;
                if (options.Count == 1) return options[0];
                var key = index == 0 ? "$attack" : "$attack:" + index;
                if (!_command.Selections.ContainsKey(key) && _command.Selections.TryGetValue("$attack", out var common) && common.Length == 1 && options.Contains(common[0]))
                    return common[0];
                return SelectOne(key, options, "Choose the monster attacked by this action.");
            }

            private bool CheckBattleClear(Context context)
            {
                if (!InBattle || LivingMonsters().Count > 0) return false;
                FinishBattle(true, context);
                return true;
            }

            private void FinishBattle(bool cleared, Context context)
            {
                if (_finishingBattle || !InBattle) return;
                _finishingBattle = true;
                var battle = State.Battle;
                var encounter = _specs.Encounter(battle.EncounterId);
                var remaining = battle.ParticipantIds.Where(id => !Player(id).Escaped && Player(id).Hp > 0).ToArray();
                try
                {
                    battle.Completed = true;
                    battle.Step = BattleStep.Completed;
                    Emit(GameEventKind.BattleEnd, context);
                    if (cleared)
                    {
                        foreach (var playerId in remaining)
                        {
                            Reward(encounter.RewardId, ActorContext(playerId));
                            foreach (var monsterId in battle.MonsterOrder)
                                Reward(_specs.Monster(battle.Monsters[monsterId].SpecId).RewardId, ActorContext(playerId));
                        }
                        foreach (var monsterId in battle.MonsterOrder)
                            if (battle.LastHitPlayerByMonster.TryGetValue(monsterId, out var killer) && remaining.Contains(killer))
                                Reward(_specs.Monster(battle.Monsters[monsterId].SpecId).FinishRewardId, ActorContext(killer));
                        if (encounter.VictoryOnClear) { State.Victorious = true; State.OutcomeReason = "Encounter objective cleared: " + encounter.Key; }
                    }
                    else battle.LastHitPlayerByMonster.Clear();
                }
                finally { _finishingBattle = false; }
                if (cleared && !State.Defeated && !State.Victorious && !string.IsNullOrEmpty(encounter.NextEncounterId))
                    StartBattle(encounter.NextEncounterId, remaining);
            }

            private void Reward(string rewardId, Context context)
            {
                if (!string.IsNullOrEmpty(rewardId)) RunGroup(_specs.Reward(rewardId).EffectGroupId, context);
            }

            private void PlayOutsideBattle(PlayerState actor, CardInstanceState[] sources)
            {
                CheckAction(actor.Id, ActionKind.PlayCard, null);
                foreach (var card in sources)
                {
                    var definition = _specs.Card(card.SpecId);
                    PayResource(actor, definition.ResourceKey, definition.Cost);
                    if (definition.Kind == CardKind.Equipment)
                    {
                        CheckAction(actor.Id, ActionKind.Equip, card.Id);
                        MoveCard(card, CardZone.Equipped);
                    }
                    var context = ActorContext(actor.Id, card.Id);
                    Emit(GameEventKind.CardUsed, context);
                    RunCardEffects(card, context);
                    if (card.Zone == CardZone.Hand)
                        MoveCard(card, definition.Kind == CardKind.Item ? CardZone.Removed : CardZone.Discard);
                }
            }

            private void PlayEmergency(PlayerState actor, CardInstanceState[] sources)
            {
                CheckAction(actor.Id, ActionKind.PlayCard, null);
                foreach (var card in sources)
                {
                    var definition = _specs.Card(card.SpecId);
                    Require(definition.Kind != CardKind.Equipment, "Hand equipment needs an equipped skill; use carried emergency equipment through ActivateAbility.");
                    PayResource(actor, definition.ResourceKey, definition.Cost);
                    var context = ActorContext(actor.Id, card.Id);
                    Emit(GameEventKind.CardUsed, context);
                    RunCardEffects(card, context);
                    if (card.Zone == CardZone.Hand)
                        MoveCard(card, definition.Kind == CardKind.Item ? CardZone.Removed : CardZone.Discard);
                }
                if (!State.Defeated) CheckBattleClear(ActorContext(actor.Id));
            }
        }
    }
}
