using System;
using System.Collections.Generic;
using System.Linq;
using Kylin.DI;

namespace Project16.CardGame.Domain
{
    /// <summary>Owns the complete rules transaction. No helper locates services or commits partial state.</summary>
    public sealed partial class GameRulesDomain : IGameRulesDomain
    {
        [Inject] private GameData _data = null;
        [Inject] private IGameSpecs _specs = null;

        public CommandResult Execute(GameCommand command)
        {
            var original = _data.CloneState();
            if (command == null) return new CommandResult(CommandResultCode.Rejected, "A command is required.", original.Revision);
            if (command.ExpectedRevision >= 0 && command.ExpectedRevision != original.Revision)
                return new CommandResult(CommandResultCode.StaleRevision, "The game changed before this command was submitted.", original.Revision);
            try
            {
                var resolution = new Resolution(original, _specs, command);
                resolution.Execute();
                _data.Commit(resolution.State);
                return new CommandResult(CommandResultCode.Success, "", _data.Revision, resolution.Events);
            }
            catch (ChoiceRequired exception)
            {
                return new CommandResult(CommandResultCode.NeedsChoice, exception.Message, original.Revision, choice: exception.Request);
            }
            catch (RuleRejected exception)
            {
                return new CommandResult(CommandResultCode.Rejected, exception.Message, original.Revision);
            }
            catch (Exception exception) when (exception is KeyNotFoundException || exception is ArgumentException || exception is OverflowException)
            {
                return new CommandResult(CommandResultCode.Rejected, "Invalid command or rule definition: " + exception.Message, original.Revision);
            }
        }

        private sealed class RuleRejected : Exception
        {
            public RuleRejected(string message) : base(message) { }
        }

        private sealed class ChoiceRequired : Exception
        {
            public ChoiceRequest Request { get; }
            public ChoiceRequired(ChoiceRequest request) : base(request.Reason) => Request = request;
        }

        private sealed partial class Resolution
        {
            public GameState State { get; }
            public readonly List<RuleEvent> Events = new();
            private readonly IGameSpecs _specs;
            private readonly GameCommand _command;
            private int _steps;
            private bool _resolvingDefeat;
            private bool _finishingBattle;

            private sealed class Context
            {
                public string ActorId;
                public string SourceId;
                public string OwnerId;
                public string SelfId;
                public string TargetId;
                public GameEventKind Event;
                public Context Copy() => (Context)MemberwiseClone();
            }

            public Resolution(GameState state, IGameSpecs specs, GameCommand command)
            {
                State = state;
                _specs = specs;
                _command = command;
                Require(specs.Rules.MaxResolutionSteps > 0, "The effect resolution budget must be positive.");
            }

            public void Execute()
            {
                Step();
                if (_command.Kind != GameCommandKind.StartRun)
                {
                    Require(State.Players.Count > 0, "Start a run before issuing game commands.");
                    Require(!State.Defeated && !State.Victorious, "The run has already ended.");
                }
                switch (_command.Kind)
                {
                    case GameCommandKind.StartRun: StartRun(); break;
                    case GameCommandKind.StartBattle: StartBattle(_command.SpecId, _command.ParticipantIds); break;
                    case GameCommandKind.PlayCards: PlayCards(false); break;
                    case GameCommandKind.Switch: PlayCards(true); break;
                    case GameCommandKind.ActivateAbility: ActivateAbility(); break;
                    case GameCommandKind.DeclineSwitch: DeclineSwitch(); break;
                    case GameCommandKind.Pass: Pass(false); break;
                    case GameCommandKind.Flee: Pass(true); break;
                    case GameCommandKind.AdvancePhase: AdvancePhase(); break;
                    case GameCommandKind.EndRound: EndRound(); break;
                    case GameCommandKind.UpgradeCard: UpgradeCardCommand(); break;
                    case GameCommandKind.Rest: Rest(); break;
                    default: throw new RuleRejected("Unsupported command.");
                }
                CheckDefeat(new Context { ActorId = State.CurrentPlayerId, OwnerId = State.CurrentPlayerId });
            }

            private void StartRun()
            {
                Require(State.Players.Count == 0, "An existing run cannot be overwritten by StartRun.");
                var scenario = _specs.Scenario(_command.SpecId);
                Require(scenario.ActorIds.Count > 0, "A scenario must define its actors.");
                State.RandomState = _command.Seed == 0 ? 1UL : _command.Seed;
                State.IsSolo = scenario.IsSolo;
                State.Round = 1;
                State.NextInstanceId = Math.Max(1, State.NextInstanceId);
                foreach (var actorId in scenario.ActorIds)
                {
                    Step();
                    var actor = _specs.Actor(actorId);
                    Require(actor.MaxHp > 0, "Actor maximum HP must be authored and positive.");
                    var id = NewId("player");
                    var player = new PlayerState
                    {
                        Id = id, ActorSpecId = actor.Key, Team = actor.Team, Hp = actor.MaxHp,
                        MaxHp = actor.MaxHp, BaseAttack = actor.BaseAttack
                    };
                    player.Tags.AddRange(actor.Tags);
                    foreach (var companionId in scenario.CompanionIds)
                    {
                        _specs.Companion(companionId);
                        player.CompanionIds.Add(companionId);
                    }
                    State.Players.Add(id, player);
                    State.PlayerOrder.Add(id);
                    foreach (var entry in _specs.DeckEntries(scenario.DeckId))
                    {
                        Require(entry.Count >= 0, "Deck entry counts must not be negative.");
                        for (var i = 0; i < entry.Count; i++) AddCard(id, entry.CardId, CardZone.Deck);
                    }
                    Shuffle(player.Deck);
                    Draw(player, State.IsSolo ? _specs.Rules.SoloDrawCount : _specs.Rules.NormalDrawCount);
                }
                State.LeaderPlayerId = State.PlayerOrder[0];
                State.CurrentPlayerId = State.LeaderPlayerId;
                EnterPhase(_specs.Rules.InitialPhaseId);
                Log(RuleEvents.RunStarted, State.CurrentPlayerId);
                if (!string.IsNullOrEmpty(scenario.EncounterId)) StartBattle(scenario.EncounterId, State.PlayerOrder);
            }

            private void AdvancePhase()
            {
                Require(!InBattle, "Finish or leave the battle before advancing the global phase.");
                var phase = _specs.Phase(State.PhaseId);
                if (phase.Kind == PhaseKind.End) { EndRound(); return; }
                var context = ActorContext(State.CurrentPlayerId);
                RunGroup(phase.ExitGroupId, context);
                Emit(GameEventKind.PhaseEnd, context);
                Require(!string.IsNullOrEmpty(phase.NextPhaseId), "The next global phase is not configured.");
                EnterPhase(phase.NextPhaseId);
            }

            private void EnterPhase(string phaseId)
            {
                var phase = _specs.Phase(phaseId);
                State.PhaseId = phase.Key;
                State.PhaseSerial = checked(State.PhaseSerial + 1);
                var context = ActorContext(State.CurrentPlayerId);
                RunGroup(phase.EnterGroupId, context);
                Emit(GameEventKind.PhaseStart, context);
            }

            private void EndRound()
            {
                Require(!InBattle, "A global round cannot end during a live battle.");
                var phase = _specs.Phase(State.PhaseId);
                Require(phase.Kind == PhaseKind.End, "EndRound is only available in the authored End phase.");
                var context = ActorContext(State.CurrentPlayerId);
                RunGroup(phase.ExitGroupId, context);
                Emit(GameEventKind.PhaseEnd, context);
                Emit(GameEventKind.RoundEnd, context);
                if (State.Battle != null)
                    foreach (var monster in State.Battle.Monsters.Values)
                        if (monster.Hp > 0) { monster.Damage = 0; monster.Hp = monster.MaxHp; }
                foreach (var playerId in State.PlayerOrder)
                {
                    var player = Player(playerId);
                    foreach (var cardId in player.Hand.ToArray()) MoveCard(Card(cardId), CardZone.Discard);
                    Draw(player, State.IsSolo ? _specs.Rules.SoloDrawCount : _specs.Rules.NormalDrawCount);
                    player.Escaped = false;
                }
                var leaderIndex = State.PlayerOrder.IndexOf(State.LeaderPlayerId);
                State.LeaderPlayerId = State.PlayerOrder[(leaderIndex + 1) % State.PlayerOrder.Count];
                State.CurrentPlayerId = State.LeaderPlayerId;
                State.Round = checked(State.Round + 1);
                EnterPhase(string.IsNullOrEmpty(phase.NextPhaseId) ? _specs.Rules.InitialPhaseId : phase.NextPhaseId);
            }

            private void Rest()
            {
                Require(!InBattle && _specs.Phase(State.PhaseId).Kind == PhaseKind.Exploration,
                    "Rest is only available outside battle in Exploration.");
                var player = Player(_command.ActorId);
                CheckAction(player.Id, ActionKind.Rest, null);
                var actionKey = "exploration-action:" + State.Round + ":" + player.Id;
                Require(Counter(actionKey) == 0, "This actor already used their exploration action this round.");
                Require(_command.SourceIds.Count <= 1, "Rest may remove at most one hand card.");
                var count = _specs.Rules.RestHeal;
                if (_command.SourceIds.Count == 1)
                {
                    var card = Card(_command.SourceIds[0]);
                    Require(card.OwnerId == player.Id && card.Zone == CardZone.Hand, "Rest cost must be a card from that player's hand.");
                    MoveCard(card, CardZone.Removed);
                    count = _specs.Rules.RestWithExileHeal;
                }
                Heal(player.Id, count, ActorContext(player.Id));
                Increment(actionKey);
            }

            private void UpgradeCardCommand()
            {
                Require(!InBattle, "Permanent skill upgrades must be attached outside battle.");
                Require(_command.SourceIds.Count == 1, "Choose one skill to upgrade.");
                var card = Card(_command.SourceIds[0]);
                Require(card.OwnerId == _command.ActorId && card.Zone != CardZone.Removed, "The actor must own the upgrade target.");
                AttachUpgrade(card, _command.SpecId, ActorContext(_command.ActorId, card.Id));
            }

            private void AttachUpgrade(CardInstanceState card, string upgradeId, Context context)
            {
                var upgrade = _specs.Upgrade(upgradeId);
                var definition = _specs.Card(card.SpecId);
                Require(definition.Kind == CardKind.Skill, "Only skill cards accept skill upgrades.");
                Require(string.IsNullOrEmpty(upgrade.RequiredCardTag) || HasTag(card.Id, upgrade.RequiredCardTag), "The skill does not satisfy the upgrade tag.");
                Require(string.IsNullOrEmpty(upgrade.CompanionId) || card.CompanionId == upgrade.CompanionId, "The upgrade belongs to another companion.");
                Require(card.UpgradeIds.Count(id => id == upgradeId) < upgrade.MaxAttachments, "The upgrade attachment limit was reached.");
                card.UpgradeIds.Add(upgradeId);
                if (!string.IsNullOrEmpty(upgrade.EffectGroupId)) card.AttachedEffectGroupIds.Add(upgrade.EffectGroupId);
                if (!string.IsNullOrEmpty(upgrade.ModifierId)) card.AttachedModifierIds.Add(upgrade.ModifierId);
                Log(RuleEvents.CardUpgraded, context.ActorId, card.Id, upgradeId);
            }

            private bool InBattle => State.Battle != null && !State.Battle.Completed && State.Battle.Step != BattleStep.None;
            private void Step()
            {
                if (++_steps > _specs.Rules.MaxResolutionSteps)
                    throw new RuleRejected("The authored effects exceeded the command resolution budget; no state was committed.");
            }
            private static void Require(bool condition, string message)
            {
                if (!condition) throw new RuleRejected(message);
            }
            private string NewId(string prefix) => prefix + ":" + checked(State.NextInstanceId++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            private PlayerState Player(string id)
            {
                Require(id != null && State.Players.ContainsKey(id), "Unknown player: " + id);
                return State.Players[id];
            }
            private CardInstanceState Card(string id)
            {
                Require(id != null && State.Cards.ContainsKey(id), "Unknown card instance: " + id);
                return State.Cards[id];
            }
            private Context ActorContext(string actorId, string sourceId = null) => new()
            {
                ActorId = actorId, OwnerId = actorId, SelfId = actorId, SourceId = sourceId, TargetId = actorId
            };
            private void Log(string kind, string actorId, string targetId = null, string sourceId = null, int amount = 0) =>
                Events.Add(new RuleEvent(kind, actorId, targetId, sourceId, amount));
        }
    }
}
