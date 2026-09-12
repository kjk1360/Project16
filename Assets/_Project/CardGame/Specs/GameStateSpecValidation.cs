using System;
using System.Collections.Generic;
using System.Linq;
using Project16.Foundation.Specs;

namespace Project16.CardGame
{
    /// <summary>
    /// Composition boundary for a loaded save and the current immutable catalog. It never mutates,
    /// repairs, or resets the save: missing authored definitions require an explicit migration.
    /// </summary>
    public static class GameStateSpecValidation
    {
        public static void Validate(GameState state, IGameSpecs specs)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (specs == null) throw new ArgumentNullException(nameof(specs));
            try { state.Validate(); }
            catch (InvalidOperationException exception)
            { throw new SpecValidationException("Loaded game structure is invalid: " + exception.Message); }

            var errors = new List<string>();
            void Check(bool valid, string message) { if (!valid) errors.Add(message); }
            void Resolve(Action lookup, string context)
            {
                try { lookup(); }
                catch (Exception exception) when (exception is KeyNotFoundException || exception is ArgumentException)
                { errors.Add(context + ": " + exception.Message); }
            }

            if (state.Players.Count > 0)
            {
                Check(!string.IsNullOrWhiteSpace(state.PhaseId), "An active run needs a phase.");
                Check(!string.IsNullOrWhiteSpace(state.CurrentPlayerId), "An active run needs a current player.");
                Check(!string.IsNullOrWhiteSpace(state.LeaderPlayerId), "An active run needs a leader.");
                Check(!state.IsSolo || state.Players.Count == 1, "A solo run must have one player.");
            }
            if (!string.IsNullOrEmpty(state.PhaseId)) Resolve(() => specs.Phase(state.PhaseId), "Phase");
            foreach (PlayerState player in state.Players.Values)
            {
                Resolve(() => specs.Actor(player.ActorSpecId), "Player " + player.Id);
                foreach (string id in player.CompanionIds) Resolve(() => specs.Companion(id), "Player companion " + player.Id);
                Check(player.Resources.Values.All(value => value >= 0), "Player resources cannot be negative: " + player.Id);
            }
            foreach (CardInstanceState card in state.Cards.Values)
            {
                Resolve(() => specs.Card(card.SpecId), "Card " + card.Id);
                if (!string.IsNullOrEmpty(card.CompanionId)) Resolve(() => specs.Companion(card.CompanionId), "Card companion " + card.Id);
                foreach (string id in card.UpgradeIds) Resolve(() => specs.Upgrade(id), "Card upgrade " + card.Id);
                foreach (string id in card.AttachedEffectGroupIds)
                {
                    Check(!string.IsNullOrWhiteSpace(id), "An attached group needs a key: " + card.Id);
                    Resolve(() => specs.Effects(id), "Attached group " + card.Id);
                }
                foreach (string id in card.AttachedModifierIds)
                    Resolve(() =>
                    {
                        var modifier = specs.Modifier(id);
                        Check(modifier.Kind == ModifierKind.PreventAction || (modifier.Kind == ModifierKind.Stat && modifier.Stat == StatKind.Attack),
                            "Unsupported intrinsic skill modifier: " + id);
                    }, "Attached modifier " + card.Id);
                Check(card.AbilityUseCounts.Values.All(value => value >= 0), "Ability usage counts cannot be negative: " + card.Id);
                if (!string.IsNullOrEmpty(card.EquippedTo))
                    Check(card.Zone == CardZone.Equipped && state.Cards.TryGetValue(card.EquippedTo, out CardInstanceState skill) && skill.OwnerId == card.OwnerId,
                        "Equipment must refer to a skill owned by the same player: " + card.Id);
            }
            foreach (ModifierState modifier in state.Modifiers)
                Resolve(() => specs.Modifier(modifier.SpecId), "Modifier " + modifier.Id);

            BattleState battle = state.Battle;
            if (battle.Step != BattleStep.None)
            {
                Resolve(() => specs.Encounter(battle.EncounterId), "Battle encounter");
                Check(battle.Completed == (battle.Step == BattleStep.Completed), "Battle completion flags are inconsistent.");
                if (!battle.Completed)
                {
                    Check(battle.ParticipantIds.Contains(state.CurrentPlayerId), "The current actor must participate in the active battle.");
                    Check(battle.ActivePlayerIndex >= 0 && battle.ActivePlayerIndex < battle.ParticipantIds.Count,
                        "The battle player index is invalid.");
                }
            }
            else if (!string.IsNullOrEmpty(battle.EncounterId)) Resolve(() => specs.Encounter(battle.EncounterId), "Battle encounter");
            foreach (MonsterState monster in battle.Monsters.Values) Resolve(() => specs.Monster(monster.SpecId), "Monster " + monster.Id);
            if (!string.IsNullOrEmpty(battle.ActiveCardId)) Check(state.Cards.ContainsKey(battle.ActiveCardId), "The active battle card is unknown.");
            if (!string.IsNullOrEmpty(battle.PendingResponsePlayerId)) Check(battle.ParticipantIds.Contains(battle.PendingResponsePlayerId), "The response player is not a participant.");
            if (!string.IsNullOrEmpty(battle.LastActingPlayerId)) Check(state.Players.ContainsKey(battle.LastActingPlayerId), "The last acting player is unknown.");
            foreach (var lastHit in battle.LastHitPlayerByMonster)
                Check(battle.Monsters.ContainsKey(lastHit.Key) && state.Players.ContainsKey(lastHit.Value), "The final-hit attribution is invalid.");
            if (errors.Count > 0) throw new SpecValidationException(string.Join(Environment.NewLine, errors));
        }
    }
}
