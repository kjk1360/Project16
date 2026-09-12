using System;
using Kylin.DI;
using Project16.CardGame.Application;
using Project16.CardGame.Domain;
using Project16.Foundation.Composition;
using Project16.Foundation.Persistence;
using UnityEngine;

namespace Project16.CardGame.Composition
{
    [CreateAssetMenu(menuName = "Project16/Card Game Module")]
    public sealed class CardGameModule : FoundationModule
    {
        [SerializeField] private TextAsset _specDatabase;
        public TextAsset SpecDatabase => _specDatabase;

        public override void ConfigureApplication(ScopeBuilder builder)
        {
            if (_specDatabase == null) throw new InvalidOperationException("Assign a validated BG Spec asset to CardGameModule.");
            var specs = GameSpecLoader.Load(_specDatabase.bytes);
            builder.Bind<IGameSpecs>().FromInstance(specs);
        }

        public override void ConfigureSession(ScopeBuilder builder, LocalSaveSession saves)
        {
            var loaded = saves.Load(GameData.SaveKey, GameData.CurrentSchema, () => new GameState());
            builder.Bind<GameData>().FromFactory(() => new GameData(loaded)).AlsoBind<IGameData>().AsScoped();
            builder.Bind<IGameRulesDomain>().To<GameRulesDomain>().AsScoped();
            builder.Bind<IGameApplication>().To<GameApplication>().AsScoped();
        }

        public override void OnSessionBuilt(IScope scope, LocalSaveSession saves)
        {
            var data = scope.Resolve<GameData>();
            GameStateSpecValidation.Validate(data.CaptureSnapshot(), scope.Resolve<IGameSpecs>());
            saves.Track(data);
        }
    }
}
