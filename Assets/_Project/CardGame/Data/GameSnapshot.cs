using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Project16.CardGame
{
    /// <summary>Immutable read facade. ToState returns an independent mutable simulation copy.</summary>
    public sealed class GameSnapshot
    {
        private readonly GameState _state;
        internal GameSnapshot(GameState detached)
        {
            _state = detached;
            var players = new Dictionary<string, PlayerSnapshot>();
            foreach (var pair in detached.Players) players.Add(pair.Key, new PlayerSnapshot(pair.Value));
            Players = new ReadOnlyDictionary<string, PlayerSnapshot>(players);
            var cards = new Dictionary<string, CardSnapshot>();
            foreach (var pair in detached.Cards) cards.Add(pair.Key, new CardSnapshot(pair.Value));
            Cards = new ReadOnlyDictionary<string, CardSnapshot>(cards);
            var modifiers = new List<ModifierSnapshot>();
            foreach (ModifierState modifier in detached.Modifiers) modifiers.Add(new ModifierSnapshot(modifier));
            Modifiers = modifiers.AsReadOnly();
            Battle = new BattleSnapshot(detached.Battle);
            PlayerOrder = detached.PlayerOrder.AsReadOnly();
            Counters = new ReadOnlyDictionary<string, int>(detached.Counters);
        }
        public long Revision => _state.Revision;
        public long Version => Revision;
        public int Round => _state.Round;
        public long TurnSerial => _state.TurnSerial;
        public long PhaseSerial => _state.PhaseSerial;
        public string PhaseId => _state.PhaseId;
        public string CurrentPlayerId => _state.CurrentPlayerId;
        public string LeaderPlayerId => _state.LeaderPlayerId;
        public bool IsSolo => _state.IsSolo;
        public bool Defeated => _state.Defeated;
        public bool Victorious => _state.Victorious;
        public string OutcomeReason => _state.OutcomeReason;
        public IReadOnlyDictionary<string, PlayerSnapshot> Players { get; }
        public IReadOnlyDictionary<string, CardSnapshot> Cards { get; }
        public IReadOnlyList<ModifierSnapshot> Modifiers { get; }
        public IReadOnlyList<string> PlayerOrder { get; }
        public IReadOnlyDictionary<string, int> Counters { get; }
        public BattleSnapshot Battle { get; }
        public GameState ToState() => _state.Clone();
    }

    public sealed class PlayerSnapshot
    {
        private readonly PlayerState _state;
        internal PlayerSnapshot(PlayerState state)
        {
            _state = state; CompanionIds = state.CompanionIds.AsReadOnly(); Tags = state.Tags.AsReadOnly();
            Deck = state.Deck.AsReadOnly(); Hand = state.Hand.AsReadOnly(); Discard = state.Discard.AsReadOnly();
            Removed = state.Removed.AsReadOnly(); Equipped = state.Equipped.AsReadOnly();
            Resources = new ReadOnlyDictionary<string, int>(state.Resources);
        }
        public string Id => _state.Id;
        public string ActorSpecId => _state.ActorSpecId;
        public string Team => _state.Team;
        public int Hp => _state.Hp;
        public int MaxHp => _state.MaxHp;
        public int BaseAttack => _state.BaseAttack;
        public int TurnCount => _state.TurnCount;
        public int CardActionsUsed => _state.CardActionsUsed;
        public int ItemActionsUsed => _state.ItemActionsUsed;
        public bool Escaped => _state.Escaped;
        public IReadOnlyList<string> CompanionIds { get; }
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyList<string> Deck { get; }
        public IReadOnlyList<string> Hand { get; }
        public IReadOnlyList<string> Discard { get; }
        public IReadOnlyList<string> Removed { get; }
        public IReadOnlyList<string> Equipped { get; }
        public IReadOnlyDictionary<string, int> Resources { get; }
    }

    public sealed class CardSnapshot
    {
        private readonly CardInstanceState _state;
        internal CardSnapshot(CardInstanceState state)
        { _state = state; UpgradeIds = state.UpgradeIds.AsReadOnly(); AttachedEffectGroupIds = state.AttachedEffectGroupIds.AsReadOnly(); AttachedModifierIds = state.AttachedModifierIds.AsReadOnly(); AbilityUseCounts = new ReadOnlyDictionary<string, int>(state.AbilityUseCounts); }
        public string Id => _state.Id;
        public string SpecId => _state.SpecId;
        public string OwnerId => _state.OwnerId;
        public string CompanionId => _state.CompanionId;
        public string EquippedTo => _state.EquippedTo;
        public CardZone Zone => _state.Zone;
        public int UsesThisTurn => _state.UsesThisTurn;
        public IReadOnlyList<string> UpgradeIds { get; }
        public IReadOnlyList<string> AttachedEffectGroupIds { get; }
        public IReadOnlyList<string> AttachedModifierIds { get; }
        public IReadOnlyDictionary<string, int> AbilityUseCounts { get; }
    }

    public sealed class MonsterSnapshot
    {
        private readonly MonsterState _state;
        internal MonsterSnapshot(MonsterState state)
        { _state = state; Tags = state.Tags.AsReadOnly(); Counters = new ReadOnlyDictionary<string, int>(state.Counters); }
        public string Id => _state.Id;
        public string SpecId => _state.SpecId;
        public int Hp => _state.Hp;
        public int MaxHp => _state.MaxHp;
        public int Damage => _state.Damage;
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyDictionary<string, int> Counters { get; }
    }

    public sealed class BattleSnapshot
    {
        private readonly BattleState _state;
        internal BattleSnapshot(BattleState state)
        {
            _state = state; ParticipantIds = state.ParticipantIds.AsReadOnly(); MonsterOrder = state.MonsterOrder.AsReadOnly();
            var monsters = new Dictionary<string, MonsterSnapshot>();
            foreach (var pair in state.Monsters) monsters.Add(pair.Key, new MonsterSnapshot(pair.Value));
            Monsters = new ReadOnlyDictionary<string, MonsterSnapshot>(monsters);
            LastHitPlayerByMonster = new ReadOnlyDictionary<string, string>(state.LastHitPlayerByMonster);
        }
        public string EncounterId => _state.EncounterId;
        public BattleStep Step => _state.Step;
        public int ActivePlayerIndex => _state.ActivePlayerIndex;
        public int ExtraAttacks => _state.ExtraAttacks;
        public string ActiveCardId => _state.ActiveCardId;
        public string LastActingPlayerId => _state.LastActingPlayerId;
        public string PendingResponsePlayerId => _state.PendingResponsePlayerId;
        public bool PendingEscape => _state.PendingEscape;
        public bool Completed => _state.Completed;
        public IReadOnlyList<string> ParticipantIds { get; }
        public IReadOnlyList<string> MonsterOrder { get; }
        public IReadOnlyDictionary<string, MonsterSnapshot> Monsters { get; }
        public IReadOnlyDictionary<string, string> LastHitPlayerByMonster { get; }
    }

    public sealed class ModifierSnapshot
    {
        private readonly ModifierState _state;
        internal ModifierSnapshot(ModifierState state) { _state = state; }
        public string Id => _state.Id;
        public string SpecId => _state.SpecId;
        public string OwnerId => _state.OwnerId;
        public string TargetId => _state.TargetId;
        public string SourceId => _state.SourceId;
        public string AnchorPlayerId => _state.AnchorPlayerId;
        public int Stacks => _state.Stacks;
        public int RemainingOccurrences => _state.RemainingOccurrences;
        public int TriggerOccurrences => _state.TriggerOccurrences;
        public int AppliedRound => _state.AppliedRound;
        public long AppliedTurnSerial => _state.AppliedTurnSerial;
        public long AppliedPhaseSerial => _state.AppliedPhaseSerial;
    }
}
