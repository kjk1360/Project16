using Kylin.DI.Layered;

namespace Project16.CardGame.Domain
{
    public interface IGameRulesDomain : IDomainServiceLayer<GameData>
    {
        CommandResult Execute(GameCommand command);
    }
}
