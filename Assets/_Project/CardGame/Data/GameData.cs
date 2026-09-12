using System;
using Kylin.DI.Layered;
using Project16.Foundation.Persistence;

namespace Project16.CardGame
{
    public interface IGameData : IDataLayer
    {
        GameSnapshot Snapshot { get; }
        long Revision { get; }
    }

    /// <summary>Owns the committed run. Domain transactions never expose its mutable identity.</summary>
    public sealed class GameData : UserDataUnit<GameState>, IGameData, IDisposable
    {
        public const string SaveKey = "card-game-run";
        public const int CurrentSchema = 1;
        private GameState _state;
        private bool _disposed;

        public GameData() : this(new GameState()) { }
        public GameData(GameState state) : this(SaveLoadResult<GameState>.CreateDetached(state)) { }
        public GameData(SaveLoadResult<GameState> loaded) : base(SaveKey, CurrentSchema, loaded)
        {
            loaded.Snapshot.Validate();
            _state = loaded.Snapshot.Clone();
            _state.Revision = Revision;
        }

        public GameSnapshot Snapshot => new GameSnapshot(CloneState());
        public GameState CloneState() { CheckAlive(); return _state.Clone(); }
        public override GameState CaptureSnapshot() => CloneState();

        [OwnerOnly]
        public void Commit(GameState state)
        {
            CheckAlive();
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Validate();
            GameState committed = state.Clone();
            MarkDirty();
            committed.Revision = Revision;
            _state = committed;
        }

        public void Dispose() { _disposed = true; }
        private void CheckAlive() { if (_disposed) throw new ObjectDisposedException(nameof(GameData)); }
    }
}
