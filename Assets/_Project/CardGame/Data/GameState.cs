using System;
using System.Collections.Generic;
using MessagePack;

namespace Project16.CardGame
{
    /// <summary>Detached transaction/save DTO. Only GameData's owner may commit it.</summary>
    [MessagePackFormatter(typeof(Project16.CardGame.Persistence.GameStateFormatter))]
    public sealed class GameState
    {
        public long Revision = 1;
        public long Version { get => Revision; set => Revision = value; }
        public int Round = 1;
        public long TurnSerial;
        public long PhaseSerial;
        public string PhaseId;
        public string CurrentPlayerId;
        public string LeaderPlayerId;
        public bool IsSolo;
        public bool Defeated;
        public bool Victorious;
        public string OutcomeReason;
        public ulong RngState = 1;
        public ulong RandomState { get => RngState; set => RngState = value; }
        public long NextInstanceId = 1;
        public Dictionary<string, PlayerState> Players = new Dictionary<string, PlayerState>(StringComparer.Ordinal);
        public List<string> PlayerOrder = new List<string>();
        public Dictionary<string, CardInstanceState> Cards = new Dictionary<string, CardInstanceState>(StringComparer.Ordinal);
        public List<ModifierState> Modifiers = new List<ModifierState>();
        public BattleState Battle = new BattleState();
        public Dictionary<string, int> Counters = new Dictionary<string, int>(StringComparer.Ordinal);

        public GameState Clone()
        {
            var copy = (GameState)MemberwiseClone();
            copy.Players = new Dictionary<string, PlayerState>(StringComparer.Ordinal);
            foreach (var pair in Players) copy.Players.Add(pair.Key, pair.Value.Clone());
            copy.PlayerOrder = new List<string>(PlayerOrder);
            copy.Cards = new Dictionary<string, CardInstanceState>(StringComparer.Ordinal);
            foreach (var pair in Cards) copy.Cards.Add(pair.Key, pair.Value.Clone());
            copy.Modifiers = new List<ModifierState>();
            foreach (ModifierState modifier in Modifiers) copy.Modifiers.Add(modifier.Clone());
            copy.Battle = Battle == null ? new BattleState() : Battle.Clone();
            copy.Counters = new Dictionary<string, int>(Counters, StringComparer.Ordinal);
            return copy;
        }

        public GameState DeepClone() => Clone();

        /// <summary>Structural save invariants, independent from any particular specification version.</summary>
        public void Validate()
        {
            if (Revision < 1 || Round < 0 || TurnSerial < 0 || PhaseSerial < 0 || NextInstanceId < 1 ||
                (Defeated && Victorious)) throw new InvalidOperationException("Invalid game counters or outcome.");
            if (Players == null || PlayerOrder == null || Cards == null || Modifiers == null || Counters == null || Battle == null)
                throw new InvalidOperationException("Game collections cannot be null.");
            var ordered = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in PlayerOrder)
                if (id == null || !Players.ContainsKey(id) || !ordered.Add(id)) throw new InvalidOperationException("Invalid player order.");
            if (ordered.Count != Players.Count) throw new InvalidOperationException("Player order must include every player.");
            if (!string.IsNullOrEmpty(CurrentPlayerId) && !Players.ContainsKey(CurrentPlayerId)) throw new InvalidOperationException("Unknown current player.");
            if (!string.IsNullOrEmpty(LeaderPlayerId) && !Players.ContainsKey(LeaderPlayerId)) throw new InvalidOperationException("Unknown leader.");
            foreach (var pair in Players)
            {
                PlayerState player = pair.Value;
                if (player == null || string.IsNullOrEmpty(pair.Key) || pair.Key != player.Id || player.MaxHp <= 0 ||
                    player.Hp < 0 || player.Hp > player.MaxHp || player.TurnCount < 0 || player.CardActionsUsed < 0 || player.ItemActionsUsed < 0)
                    throw new InvalidOperationException("Invalid player state.");
                if (player.Resources == null || player.CompanionIds == null || player.Tags == null || player.Deck == null ||
                    player.Hand == null || player.Discard == null || player.Removed == null || player.Equipped == null)
                    throw new InvalidOperationException("Player collections cannot be null.");
            }
            var located = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlayerState player in Players.Values)
            {
                CheckZone(player.Deck, CardZone.Deck, player.Id, located);
                CheckZone(player.Hand, CardZone.Hand, player.Id, located);
                CheckZone(player.Discard, CardZone.Discard, player.Id, located);
                CheckZone(player.Removed, CardZone.Removed, player.Id, located);
                CheckZone(player.Equipped, CardZone.Equipped, player.Id, located);
            }
            foreach (var pair in Cards)
            {
                CardInstanceState card = pair.Value;
                if (card == null || pair.Key != card.Id || !Players.ContainsKey(card.OwnerId ?? string.Empty) ||
                    !Enum.IsDefined(typeof(CardZone), card.Zone) || card.UsesThisTurn < 0 || !located.Contains(pair.Key) ||
                    card.UpgradeIds == null || card.AttachedEffectGroupIds == null || card.AttachedModifierIds == null || card.AbilityUseCounts == null)
                    throw new InvalidOperationException("Invalid card state or zone ownership.");
            }
            var modifierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModifierState modifier in Modifiers)
                if (modifier == null || string.IsNullOrEmpty(modifier.Id) || !modifierIds.Add(modifier.Id) || modifier.Stacks < 1 ||
                    modifier.RemainingOccurrences < 0 || modifier.TriggerOccurrences < 0)
                    throw new InvalidOperationException("Invalid modifier state.");
            if (!Enum.IsDefined(typeof(BattleStep), Battle.Step) || Battle.ParticipantIds == null || Battle.Monsters == null || Battle.MonsterOrder == null || Battle.LastHitPlayerByMonster == null || Battle.ExtraAttacks < 0)
                throw new InvalidOperationException("Invalid battle state.");
            var participants = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in Battle.ParticipantIds)
                if (!Players.ContainsKey(id ?? string.Empty) || !participants.Add(id)) throw new InvalidOperationException("Invalid battle participant.");
            var monsterIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in Battle.MonsterOrder)
                if (!Battle.Monsters.ContainsKey(id ?? string.Empty) || !monsterIds.Add(id)) throw new InvalidOperationException("Invalid monster order.");
            if (monsterIds.Count != Battle.Monsters.Count) throw new InvalidOperationException("Monster order must include every monster.");
            foreach (var pair in Battle.Monsters)
                if (pair.Value == null || pair.Key != pair.Value.Id || pair.Value.MaxHp <= 0 || pair.Value.Hp < 0 ||
                    pair.Value.Hp > pair.Value.MaxHp || pair.Value.Tags == null || pair.Value.Counters == null)
                    throw new InvalidOperationException("Invalid monster state.");
        }

        private void CheckZone(List<string> ids, CardZone zone, string owner, HashSet<string> located)
        {
            foreach (string id in ids)
                if (id == null || !Cards.TryGetValue(id, out CardInstanceState card) || card == null ||
                    card.OwnerId != owner || card.Zone != zone || !located.Add(id))
                    throw new InvalidOperationException("A card must occupy exactly one matching owner zone.");
        }
    }

    public sealed class PlayerState
    {
        public string Id;
        public string ActorSpecId;
        public string Team;
        public int Hp;
        public int MaxHp;
        public int BaseAttack;
        public int TurnCount;
        public int CardActionsUsed;
        public int ItemActionsUsed;
        public bool Escaped;
        public List<string> CompanionIds = new List<string>();
        public List<string> Tags = new List<string>();
        public List<string> Deck = new List<string>();
        public List<string> Hand = new List<string>();
        public List<string> Discard = new List<string>();
        public List<string> Removed = new List<string>();
        public List<string> Equipped = new List<string>();
        public Dictionary<string, int> Resources = new Dictionary<string, int>(StringComparer.Ordinal);

        public PlayerState Clone()
        {
            var copy = (PlayerState)MemberwiseClone();
            copy.CompanionIds = new List<string>(CompanionIds); copy.Tags = new List<string>(Tags);
            copy.Deck = new List<string>(Deck); copy.Hand = new List<string>(Hand); copy.Discard = new List<string>(Discard);
            copy.Removed = new List<string>(Removed); copy.Equipped = new List<string>(Equipped);
            copy.Resources = new Dictionary<string, int>(Resources, StringComparer.Ordinal);
            return copy;
        }
    }

    public sealed class CardInstanceState
    {
        public string Id;
        public string SpecId;
        public string OwnerId;
        public string CompanionId;
        public string EquippedTo;
        public CardZone Zone;
        public int UsesThisTurn;
        public List<string> UpgradeIds = new List<string>();
        public List<string> AttachedEffectGroupIds = new List<string>();
        public List<string> AttachedModifierIds = new List<string>();
        public Dictionary<string, int> AbilityUseCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public CardInstanceState Clone()
        {
            var copy = (CardInstanceState)MemberwiseClone();
            copy.UpgradeIds = new List<string>(UpgradeIds); copy.AttachedEffectGroupIds = new List<string>(AttachedEffectGroupIds);
            copy.AttachedModifierIds = new List<string>(AttachedModifierIds);
            copy.AbilityUseCounts = new Dictionary<string, int>(AbilityUseCounts, StringComparer.Ordinal);
            return copy;
        }
    }

    public sealed class MonsterState
    {
        public string Id;
        public string SpecId;
        public int Hp;
        public int MaxHp;
        public int Damage { get => MaxHp - Hp; set => Hp = Math.Max(0, MaxHp - value); }
        public List<string> Tags = new List<string>();
        public Dictionary<string, int> Counters = new Dictionary<string, int>(StringComparer.Ordinal);
        public MonsterState Clone()
        {
            var copy = (MonsterState)MemberwiseClone();
            copy.Tags = new List<string>(Tags); copy.Counters = new Dictionary<string, int>(Counters, StringComparer.Ordinal);
            return copy;
        }
    }

    public sealed class BattleState
    {
        public string EncounterId;
        public BattleStep Step;
        public List<string> ParticipantIds = new List<string>();
        public Dictionary<string, MonsterState> Monsters = new Dictionary<string, MonsterState>(StringComparer.Ordinal);
        public List<string> MonsterOrder = new List<string>();
        public int ActivePlayerIndex;
        public int ExtraAttacks;
        public string ActiveCardId;
        public string LastActingPlayerId;
        public string PendingResponsePlayerId;
        public bool PendingEscape;
        public Dictionary<string, string> LastHitPlayerByMonster = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool Completed;
        public BattleState Clone()
        {
            var copy = (BattleState)MemberwiseClone();
            copy.ParticipantIds = new List<string>(ParticipantIds); copy.MonsterOrder = new List<string>(MonsterOrder);
            copy.Monsters = new Dictionary<string, MonsterState>(StringComparer.Ordinal);
            foreach (var pair in Monsters) copy.Monsters.Add(pair.Key, pair.Value.Clone());
            copy.LastHitPlayerByMonster = new Dictionary<string, string>(LastHitPlayerByMonster, StringComparer.Ordinal);
            return copy;
        }
    }

    public sealed class ModifierState
    {
        public string Id;
        public string SpecId;
        public string OwnerId;
        public string TargetId;
        public string SourceId;
        public string AnchorPlayerId;
        public int Stacks = 1;
        public int RemainingOccurrences;
        public int TriggerOccurrences;
        public int AppliedRound;
        public long AppliedTurnSerial;
        public long AppliedPhaseSerial;
        public long LastTriggerTurnSerial = -1;
        public long LastTriggerPhaseSerial = -1;
        public ModifierState Clone() => (ModifierState)MemberwiseClone();
    }
}
