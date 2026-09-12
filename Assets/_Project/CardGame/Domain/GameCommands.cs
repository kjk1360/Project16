using System;
using System.Collections.Generic;

namespace Project16.CardGame.Domain
{
    public enum GameCommandKind
    {
        StartRun, StartBattle, PlayCards, ActivateAbility, Switch, DeclineSwitch,
        Pass, Flee, AdvancePhase, EndRound, UpgradeCard, Rest
    }

    /// <summary>All choices belong to one atomic player operation. Keys are selector IDs from specs.</summary>
    public sealed class GameCommand
    {
        public GameCommandKind Kind { get; }
        public string ActorId { get; }
        public string SpecId { get; }
        public IReadOnlyList<string> SourceIds { get; }
        public IReadOnlyList<string> ParticipantIds { get; }
        public IReadOnlyDictionary<string, string[]> Selections { get; }
        public ulong Seed { get; }
        public long ExpectedRevision { get; }

        public GameCommand(GameCommandKind kind, string actorId = null, string specId = null,
            IEnumerable<string> sourceIds = null, IDictionary<string, string[]> selections = null,
            IEnumerable<string> participantIds = null, ulong seed = 1, long expectedRevision = -1)
        {
            Kind = kind;
            ActorId = actorId;
            SpecId = specId;
            SourceIds = Array.AsReadOnly(sourceIds == null ? Array.Empty<string>() : new List<string>(sourceIds).ToArray());
            ParticipantIds = Array.AsReadOnly(participantIds == null ? Array.Empty<string>() : new List<string>(participantIds).ToArray());
            var copied = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (selections != null)
                foreach (var entry in selections)
                    copied.Add(entry.Key, entry.Value == null ? Array.Empty<string>() : (string[])entry.Value.Clone());
            Selections = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>(copied);
            Seed = seed;
            ExpectedRevision = expectedRevision;
        }
    }

    public enum CommandResultCode { Success, Rejected, NeedsChoice, StaleRevision }

    public sealed class ChoiceRequest
    {
        public string Key { get; }
        public string Reason { get; }
        public IReadOnlyList<string> Options { get; }
        public int Minimum { get; }
        public int Maximum { get; }

        internal ChoiceRequest(string key, string reason, IEnumerable<string> options, int minimum = 1, int maximum = 1)
        {
            Key = key;
            Reason = reason;
            Options = Array.AsReadOnly(new List<string>(options).ToArray());
            Minimum = minimum;
            Maximum = maximum;
        }
    }

    public sealed class RuleEvent
    {
        public string Kind { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string SourceId { get; }
        public int Amount { get; }

        internal RuleEvent(string kind, string actorId, string targetId = null, string sourceId = null, int amount = 0)
        {
            Kind = kind;
            ActorId = actorId;
            TargetId = targetId;
            SourceId = sourceId;
            Amount = amount;
        }
    }

    public static class RuleEvents
    {
        public const string RunStarted = "run-started";
        public const string BattleStarted = "battle-started";
        public const string TurnStarted = "turn-started";
        public const string TurnEnded = "turn-ended";
        public const string PhaseStarted = "phase-started";
        public const string PhaseEnded = "phase-ended";
        public const string RoundEnded = "round-ended";
        public const string CardUsed = "card-used";
        public const string BeforeAttack = "before-attack";
        public const string AfterAttack = "after-attack";
        public const string SwitchSucceeded = "switch-succeeded";
        public const string SwitchDeclined = "switch-declined";
        public const string MonsterResponse = "monster-response";
        public const string BeforeDamage = "before-damage";
        public const string AfterDamage = "after-damage";
        public const string BeforeDefeat = "before-defeat";
        public const string MonsterDefeated = "monster-defeated";
        public const string BattleEnded = "battle-ended";
        public const string PlayerEscaped = "player-escaped";
        public const string CardMoved = "card-moved";
        public const string ModifierApplied = "modifier-applied";
        public const string ModifierRemoved = "modifier-removed";
        public const string CardUpgraded = "card-upgraded";
    }

    public sealed class CommandResult
    {
        public bool Accepted => Code == CommandResultCode.Success;
        public CommandResultCode Code { get; }
        public string Message { get; }
        public long Revision { get; }
        public IReadOnlyList<RuleEvent> Events { get; }
        public ChoiceRequest Choice { get; }

        internal CommandResult(CommandResultCode code, string message, long revision,
            IEnumerable<RuleEvent> events = null, ChoiceRequest choice = null)
        {
            Code = code;
            Message = message;
            Revision = revision;
            Events = Array.AsReadOnly(events == null ? Array.Empty<RuleEvent>() : new List<RuleEvent>(events).ToArray());
            Choice = choice;
        }
    }
}
