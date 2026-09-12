using System;
using System.Collections.Generic;

namespace Project16.Foundation.Persistence
{
    /// <summary>A detached snapshot loaded before its Data object is composed.</summary>
    public sealed class SaveLoadResult<T>
    {
        /// <summary>
        /// Creates in-memory state for previews and simulations. A detached result
        /// cannot be tracked by LocalSaveSession; use that session's Load for real saves.
        /// </summary>
        public static SaveLoadResult<T> CreateDetached(T snapshot)
        {
            if (ReferenceEquals(snapshot, null)) throw new ArgumentNullException(nameof(snapshot));
            return new SaveLoadResult<T>(snapshot, 1, true, false, new object());
        }

        internal SaveLoadResult(T snapshot, long revision, bool requiresSave,
            bool recoveredFromBackup, object token)
        {
            Snapshot = snapshot;
            Revision = revision;
            RequiresSave = requiresSave;
            RecoveredFromBackup = recoveredFromBackup;
            Token = token;
        }

        public T Snapshot { get; }
        public long Revision { get; }
        public bool RequiresSave { get; }
        public bool RecoveredFromBackup { get; }
        internal object Token { get; }
    }

    public enum SaveLoadFailure
    {
        CorruptData,
        UnsupportedVersion,
        ReadFailed,
        MigrationFailed
    }

    public sealed class LocalSaveLoadException : Exception
    {
        public LocalSaveLoadException(string key, SaveLoadFailure failure, string message,
            Exception innerException = null) : base(message, innerException)
        {
            Key = key;
            Failure = failure;
        }

        public string Key { get; }
        public SaveLoadFailure Failure { get; }
    }

    public sealed class SaveFailure
    {
        internal SaveFailure(string key, Exception exception)
        {
            Key = key;
            Exception = exception;
        }

        public string Key { get; }
        public Exception Exception { get; }
        public string Message => Exception.Message;
    }

    public sealed class SaveReport
    {
        internal SaveReport(int attemptedCount, int savedCount, List<SaveFailure> failures,
            bool isBusy = false)
        {
            AttemptedCount = attemptedCount;
            SavedCount = savedCount;
            Failures = failures.AsReadOnly();
            IsBusy = isBusy;
        }

        public int AttemptedCount { get; }
        public int SavedCount { get; }
        public IReadOnlyList<SaveFailure> Failures { get; }
        public bool IsBusy { get; }
        public bool Succeeded => !IsBusy && Failures.Count == 0;
    }

    /// <summary>
    /// Narrow filesystem boundary. Null reads mean absent files; all other failures throw.
    /// Write must return only after replacement commits, and must not throw after committing.
    /// </summary>
    public interface ILocalSaveStore
    {
        byte[] ReadPrimary(string key);
        byte[] ReadBackup(string key);
        void Write(string key, byte[] envelope, bool preserveBackup);
    }

    internal static class SaveKey
    {
        public static void Validate(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64 || key[0] < 'a' || key[0] > 'z')
                throw new ArgumentException("Save keys must start with a lowercase letter and contain at most 64 characters.", nameof(key));

            foreach (char character in key)
            {
                if ((character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') || character == '-' || character == '_') continue;
                throw new ArgumentException("Save keys may contain only lowercase letters, digits, hyphens and underscores.", nameof(key));
            }

            bool numberedDevice = key.Length == 4 && key[3] >= '1' && key[3] <= '9' &&
                (key.StartsWith("com", StringComparison.Ordinal) || key.StartsWith("lpt", StringComparison.Ordinal));
            if (key == "con" || key == "prn" || key == "aux" || key == "nul" || numberedDevice)
                throw new ArgumentException("Save keys must not use reserved device names.", nameof(key));
        }
    }
}
