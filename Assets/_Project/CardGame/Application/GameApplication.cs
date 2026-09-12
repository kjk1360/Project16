using System.Collections.Generic;
using Kylin.DI;
using Kylin.DI.Layered;
using Project16.CardGame.Domain;

namespace Project16.CardGame.Application
{
    public interface IGameApplication : IApplicationServiceLayer
    {
        CommandResult Execute(GameCommand command);
        CommandResult StartRun(string scenarioId, ulong seed = 1);
        CommandResult StartBattle(string encounterId, IEnumerable<string> participants = null);
        CommandResult PlayCards(string actorId, IEnumerable<string> cardIds, IDictionary<string, string[]> selections = null);
        CommandResult ActivateAbility(string actorId, string sourceId, string groupId, IDictionary<string, string[]> selections = null);
        CommandResult Switch(string actorId, IEnumerable<string> cardIds, IDictionary<string, string[]> selections = null);
        CommandResult DeclineSwitch(string actorId, IDictionary<string, string[]> selections = null);
        CommandResult Pass(string actorId);
        CommandResult Flee(string actorId);
        CommandResult AdvancePhase();
        CommandResult EndRound();
        CommandResult UpgradeCard(string actorId, string cardId, string upgradeId);
        CommandResult Rest(string actorId, string removeCardId = null);
    }

    /// <summary>One public user operation delegates to one transactional Domain owner.</summary>
    public sealed class GameApplication : IGameApplication
    {
        [Inject] private IGameRulesDomain _rules = null;

        public CommandResult Execute(GameCommand command) => _rules.Execute(command);
        public CommandResult StartRun(string scenarioId, ulong seed = 1) =>
            Execute(new GameCommand(GameCommandKind.StartRun, specId: scenarioId, seed: seed));
        public CommandResult StartBattle(string encounterId, IEnumerable<string> participants = null) =>
            Execute(new GameCommand(GameCommandKind.StartBattle, specId: encounterId, participantIds: participants));
        public CommandResult PlayCards(string actorId, IEnumerable<string> cardIds, IDictionary<string, string[]> selections = null) =>
            Execute(new GameCommand(GameCommandKind.PlayCards, actorId, sourceIds: cardIds, selections: selections));
        public CommandResult ActivateAbility(string actorId, string sourceId, string groupId, IDictionary<string, string[]> selections = null) =>
            Execute(new GameCommand(GameCommandKind.ActivateAbility, actorId, groupId, new[] { sourceId }, selections));
        public CommandResult Switch(string actorId, IEnumerable<string> cardIds, IDictionary<string, string[]> selections = null) =>
            Execute(new GameCommand(GameCommandKind.Switch, actorId, sourceIds: cardIds, selections: selections));
        public CommandResult DeclineSwitch(string actorId, IDictionary<string, string[]> selections = null) =>
            Execute(new GameCommand(GameCommandKind.DeclineSwitch, actorId, selections: selections));
        public CommandResult Pass(string actorId) => Execute(new GameCommand(GameCommandKind.Pass, actorId));
        public CommandResult Flee(string actorId) => Execute(new GameCommand(GameCommandKind.Flee, actorId));
        public CommandResult AdvancePhase() => Execute(new GameCommand(GameCommandKind.AdvancePhase));
        public CommandResult EndRound() => Execute(new GameCommand(GameCommandKind.EndRound));
        public CommandResult UpgradeCard(string actorId, string cardId, string upgradeId) =>
            Execute(new GameCommand(GameCommandKind.UpgradeCard, actorId, upgradeId, new[] { cardId }));
        public CommandResult Rest(string actorId, string removeCardId = null) =>
            Execute(new GameCommand(GameCommandKind.Rest, actorId,
                sourceIds: removeCardId == null ? null : new[] { removeCardId }));
    }
}
