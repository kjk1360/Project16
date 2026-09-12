using System;
using MessagePack;
using MessagePack.Formatters;

namespace Project16.CardGame.Persistence
{
    /// <summary>
    /// Schema 1 uses fixed positional arrays and explicit nested codecs. No dynamic or
    /// contractless formatter is required. Unknown slots are rejected, not discarded.
    /// Version/RandomState aliases and computed MonsterState.Damage are not extra fields.
    /// Composition preserves this public formatter constructor for IL2CPP.
    /// </summary>
    public sealed partial class GameStateFormatter : IMessagePackFormatter<GameState>
    {
        public void Serialize(ref MessagePackWriter writer, GameState value, MessagePackSerializerOptions options)
        {
            if (value == null) throw Invalid("Game snapshot is null.");
            value.Validate();
            WriteGameState(ref writer, value, new CodecLimits(options));
        }

        public GameState Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            var state = ReadGameState(ref reader, new CodecLimits(options));
            try { state.Validate(); }
            catch (InvalidOperationException exception)
            {
                throw new MessagePackSerializationException("Game snapshot invariants are invalid.", exception);
            }
            return state;
        }

        // Slots 0..18: Revision, Round, TurnSerial, PhaseSerial, PhaseId, CurrentPlayerId, LeaderPlayerId, IsSolo, Defeated, Victorious, OutcomeReason, RngState, NextInstanceId, Players, PlayerOrder, Cards, Modifiers, Battle, Counters.
        private static void WriteGameState(ref MessagePackWriter writer, GameState value, CodecLimits limits)
        {
            if (value == null) throw Invalid("GameState is null.");
            writer.WriteArrayHeader(19);
            writer.Write(value.Revision);
            writer.Write(value.Round);
            writer.Write(value.TurnSerial);
            writer.Write(value.PhaseSerial);
            WriteText(ref writer, value.PhaseId, limits);
            WriteText(ref writer, value.CurrentPlayerId, limits);
            WriteText(ref writer, value.LeaderPlayerId, limits);
            writer.Write(value.IsSolo);
            writer.Write(value.Defeated);
            writer.Write(value.Victorious);
            WriteText(ref writer, value.OutcomeReason, limits);
            writer.Write(value.RngState);
            writer.Write(value.NextInstanceId);
            WriteMap(ref writer, value.Players, limits, WritePlayerState);
            WriteList(ref writer, value.PlayerOrder, limits, WriteRequiredText);
            WriteMap(ref writer, value.Cards, limits, WriteCardInstanceState);
            WriteList(ref writer, value.Modifiers, limits, WriteModifierState);
            WriteBattleState(ref writer, value.Battle, limits);
            WriteMap(ref writer, value.Counters, limits, WriteNumber);
        }

        private static GameState ReadGameState(ref MessagePackReader reader, CodecLimits limits)
        {
            ReadShape(ref reader, 19, "GameState");
            limits.Options.Security.DepthStep(ref reader);
            try
            {
                return new GameState
                {
                    Revision = reader.ReadInt64(),
                    Round = reader.ReadInt32(),
                    TurnSerial = reader.ReadInt64(),
                    PhaseSerial = reader.ReadInt64(),
                    PhaseId = ReadText(ref reader, limits),
                    CurrentPlayerId = ReadText(ref reader, limits),
                    LeaderPlayerId = ReadText(ref reader, limits),
                    IsSolo = reader.ReadBoolean(),
                    Defeated = reader.ReadBoolean(),
                    Victorious = reader.ReadBoolean(),
                    OutcomeReason = ReadText(ref reader, limits),
                    RngState = reader.ReadUInt64(),
                    NextInstanceId = reader.ReadInt64(),
                    Players = ReadMap(ref reader, limits, ReadPlayerState),
                    PlayerOrder = ReadList(ref reader, limits, ReadRequiredText),
                    Cards = ReadMap(ref reader, limits, ReadCardInstanceState),
                    Modifiers = ReadList(ref reader, limits, ReadModifierState),
                    Battle = ReadBattleState(ref reader, limits),
                    Counters = ReadMap(ref reader, limits, ReadNumber),
                };
            }
            finally { reader.Depth--; }
        }

        // Slots 0..17: Id, ActorSpecId, Team, Hp, MaxHp, BaseAttack, TurnCount, CardActionsUsed, ItemActionsUsed, Escaped, CompanionIds, Tags, Deck, Hand, Discard, Removed, Equipped, Resources.
        private static void WritePlayerState(ref MessagePackWriter writer, PlayerState value, CodecLimits limits)
        {
            if (value == null) throw Invalid("PlayerState is null.");
            writer.WriteArrayHeader(18);
            WriteRequiredText(ref writer, value.Id, limits);
            WriteRequiredText(ref writer, value.ActorSpecId, limits);
            WriteText(ref writer, value.Team, limits);
            writer.Write(value.Hp);
            writer.Write(value.MaxHp);
            writer.Write(value.BaseAttack);
            writer.Write(value.TurnCount);
            writer.Write(value.CardActionsUsed);
            writer.Write(value.ItemActionsUsed);
            writer.Write(value.Escaped);
            WriteList(ref writer, value.CompanionIds, limits, WriteRequiredText);
            WriteList(ref writer, value.Tags, limits, WriteRequiredText);
            WriteList(ref writer, value.Deck, limits, WriteRequiredText);
            WriteList(ref writer, value.Hand, limits, WriteRequiredText);
            WriteList(ref writer, value.Discard, limits, WriteRequiredText);
            WriteList(ref writer, value.Removed, limits, WriteRequiredText);
            WriteList(ref writer, value.Equipped, limits, WriteRequiredText);
            WriteMap(ref writer, value.Resources, limits, WriteNumber);
        }

        private static PlayerState ReadPlayerState(ref MessagePackReader reader, CodecLimits limits)
        {
            ReadShape(ref reader, 18, "PlayerState");
            limits.Options.Security.DepthStep(ref reader);
            try
            {
                return new PlayerState
                {
                    Id = ReadRequiredText(ref reader, limits),
                    ActorSpecId = ReadRequiredText(ref reader, limits),
                    Team = ReadText(ref reader, limits),
                    Hp = reader.ReadInt32(),
                    MaxHp = reader.ReadInt32(),
                    BaseAttack = reader.ReadInt32(),
                    TurnCount = reader.ReadInt32(),
                    CardActionsUsed = reader.ReadInt32(),
                    ItemActionsUsed = reader.ReadInt32(),
                    Escaped = reader.ReadBoolean(),
                    CompanionIds = ReadList(ref reader, limits, ReadRequiredText),
                    Tags = ReadList(ref reader, limits, ReadRequiredText),
                    Deck = ReadList(ref reader, limits, ReadRequiredText),
                    Hand = ReadList(ref reader, limits, ReadRequiredText),
                    Discard = ReadList(ref reader, limits, ReadRequiredText),
                    Removed = ReadList(ref reader, limits, ReadRequiredText),
                    Equipped = ReadList(ref reader, limits, ReadRequiredText),
                    Resources = ReadMap(ref reader, limits, ReadNumber),
                };
            }
            finally { reader.Depth--; }
        }

        // Slots 0..10: Id, SpecId, OwnerId, CompanionId, EquippedTo, Zone, UsesThisTurn, UpgradeIds, AttachedEffectGroupIds, AttachedModifierIds, AbilityUseCounts.
        private static void WriteCardInstanceState(ref MessagePackWriter writer, CardInstanceState value, CodecLimits limits)
        {
            if (value == null) throw Invalid("CardInstanceState is null.");
            writer.WriteArrayHeader(11);
            WriteRequiredText(ref writer, value.Id, limits);
            WriteRequiredText(ref writer, value.SpecId, limits);
            WriteRequiredText(ref writer, value.OwnerId, limits);
            WriteText(ref writer, value.CompanionId, limits);
            WriteText(ref writer, value.EquippedTo, limits);
            writer.Write((int)value.Zone);
            writer.Write(value.UsesThisTurn);
            WriteList(ref writer, value.UpgradeIds, limits, WriteRequiredText);
            WriteList(ref writer, value.AttachedEffectGroupIds, limits, WriteRequiredText);
            WriteList(ref writer, value.AttachedModifierIds, limits, WriteRequiredText);
            WriteMap(ref writer, value.AbilityUseCounts, limits, WriteNumber);
        }

        private static CardInstanceState ReadCardInstanceState(ref MessagePackReader reader, CodecLimits limits)
        {
            ReadShape(ref reader, 11, "CardInstanceState");
            limits.Options.Security.DepthStep(ref reader);
            try
            {
                return new CardInstanceState
                {
                    Id = ReadRequiredText(ref reader, limits),
                    SpecId = ReadRequiredText(ref reader, limits),
                    OwnerId = ReadRequiredText(ref reader, limits),
                    CompanionId = ReadText(ref reader, limits),
                    EquippedTo = ReadText(ref reader, limits),
                    Zone = (CardZone)reader.ReadInt32(),
                    UsesThisTurn = reader.ReadInt32(),
                    UpgradeIds = ReadList(ref reader, limits, ReadRequiredText),
                    AttachedEffectGroupIds = ReadList(ref reader, limits, ReadRequiredText),
                    AttachedModifierIds = ReadList(ref reader, limits, ReadRequiredText),
                    AbilityUseCounts = ReadMap(ref reader, limits, ReadNumber),
                };
            }
            finally { reader.Depth--; }
        }

        // Slots 0..5: Id, SpecId, Hp, MaxHp, Tags, Counters.
        private static void WriteMonsterState(ref MessagePackWriter writer, MonsterState value, CodecLimits limits)
        {
            if (value == null) throw Invalid("MonsterState is null.");
            writer.WriteArrayHeader(6);
            WriteRequiredText(ref writer, value.Id, limits);
            WriteRequiredText(ref writer, value.SpecId, limits);
            writer.Write(value.Hp);
            writer.Write(value.MaxHp);
            WriteList(ref writer, value.Tags, limits, WriteRequiredText);
            WriteMap(ref writer, value.Counters, limits, WriteNumber);
        }

        private static MonsterState ReadMonsterState(ref MessagePackReader reader, CodecLimits limits)
        {
            ReadShape(ref reader, 6, "MonsterState");
            limits.Options.Security.DepthStep(ref reader);
            try
            {
                return new MonsterState
                {
                    Id = ReadRequiredText(ref reader, limits),
                    SpecId = ReadRequiredText(ref reader, limits),
                    Hp = reader.ReadInt32(),
                    MaxHp = reader.ReadInt32(),
                    Tags = ReadList(ref reader, limits, ReadRequiredText),
                    Counters = ReadMap(ref reader, limits, ReadNumber),
                };
            }
            finally { reader.Depth--; }
        }

        // Slots 0..12: EncounterId, Step, ParticipantIds, Monsters, MonsterOrder, ActivePlayerIndex, ExtraAttacks, ActiveCardId, LastActingPlayerId, PendingResponsePlayerId, PendingEscape, LastHitPlayerByMonster, Completed.
        private static void WriteBattleState(ref MessagePackWriter writer, BattleState value, CodecLimits limits)
        {
            if (value == null) throw Invalid("BattleState is null.");
            writer.WriteArrayHeader(13);
            WriteText(ref writer, value.EncounterId, limits);
            writer.Write((int)value.Step);
            WriteList(ref writer, value.ParticipantIds, limits, WriteRequiredText);
            WriteMap(ref writer, value.Monsters, limits, WriteMonsterState);
            WriteList(ref writer, value.MonsterOrder, limits, WriteRequiredText);
            writer.Write(value.ActivePlayerIndex);
            writer.Write(value.ExtraAttacks);
            WriteText(ref writer, value.ActiveCardId, limits);
            WriteText(ref writer, value.LastActingPlayerId, limits);
            WriteText(ref writer, value.PendingResponsePlayerId, limits);
            writer.Write(value.PendingEscape);
            WriteMap(ref writer, value.LastHitPlayerByMonster, limits, WriteRequiredText);
            writer.Write(value.Completed);
        }

        private static BattleState ReadBattleState(ref MessagePackReader reader, CodecLimits limits)
        {
            ReadShape(ref reader, 13, "BattleState");
            limits.Options.Security.DepthStep(ref reader);
            try
            {
                return new BattleState
                {
                    EncounterId = ReadText(ref reader, limits),
                    Step = (BattleStep)reader.ReadInt32(),
                    ParticipantIds = ReadList(ref reader, limits, ReadRequiredText),
                    Monsters = ReadMap(ref reader, limits, ReadMonsterState),
                    MonsterOrder = ReadList(ref reader, limits, ReadRequiredText),
                    ActivePlayerIndex = reader.ReadInt32(),
                    ExtraAttacks = reader.ReadInt32(),
                    ActiveCardId = ReadText(ref reader, limits),
                    LastActingPlayerId = ReadText(ref reader, limits),
                    PendingResponsePlayerId = ReadText(ref reader, limits),
                    PendingEscape = reader.ReadBoolean(),
                    LastHitPlayerByMonster = ReadMap(ref reader, limits, ReadRequiredText),
                    Completed = reader.ReadBoolean(),
                };
            }
            finally { reader.Depth--; }
        }

        // Slots 0..13: Id, SpecId, OwnerId, TargetId, SourceId, AnchorPlayerId, Stacks, RemainingOccurrences, TriggerOccurrences, AppliedRound, AppliedTurnSerial, AppliedPhaseSerial, LastTriggerTurnSerial, LastTriggerPhaseSerial.
        private static void WriteModifierState(ref MessagePackWriter writer, ModifierState value, CodecLimits limits)
        {
            if (value == null) throw Invalid("ModifierState is null.");
            writer.WriteArrayHeader(14);
            WriteRequiredText(ref writer, value.Id, limits);
            WriteRequiredText(ref writer, value.SpecId, limits);
            WriteText(ref writer, value.OwnerId, limits);
            WriteText(ref writer, value.TargetId, limits);
            WriteText(ref writer, value.SourceId, limits);
            WriteText(ref writer, value.AnchorPlayerId, limits);
            writer.Write(value.Stacks);
            writer.Write(value.RemainingOccurrences);
            writer.Write(value.TriggerOccurrences);
            writer.Write(value.AppliedRound);
            writer.Write(value.AppliedTurnSerial);
            writer.Write(value.AppliedPhaseSerial);
            writer.Write(value.LastTriggerTurnSerial);
            writer.Write(value.LastTriggerPhaseSerial);
        }

        private static ModifierState ReadModifierState(ref MessagePackReader reader, CodecLimits limits)
        {
            ReadShape(ref reader, 14, "ModifierState");
            limits.Options.Security.DepthStep(ref reader);
            try
            {
                return new ModifierState
                {
                    Id = ReadRequiredText(ref reader, limits),
                    SpecId = ReadRequiredText(ref reader, limits),
                    OwnerId = ReadText(ref reader, limits),
                    TargetId = ReadText(ref reader, limits),
                    SourceId = ReadText(ref reader, limits),
                    AnchorPlayerId = ReadText(ref reader, limits),
                    Stacks = reader.ReadInt32(),
                    RemainingOccurrences = reader.ReadInt32(),
                    TriggerOccurrences = reader.ReadInt32(),
                    AppliedRound = reader.ReadInt32(),
                    AppliedTurnSerial = reader.ReadInt64(),
                    AppliedPhaseSerial = reader.ReadInt64(),
                    LastTriggerTurnSerial = reader.ReadInt64(),
                    LastTriggerPhaseSerial = reader.ReadInt64(),
                };
            }
            finally { reader.Depth--; }
        }
    }
}
