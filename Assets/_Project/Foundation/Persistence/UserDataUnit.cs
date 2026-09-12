using System;
using Kylin.DI.Layered;
using MessagePack;

namespace Project16.Foundation.Persistence
{
    internal interface ILocalSaveUnit
    {
        string Key { get; }
        int SchemaVersion { get; }
        long Revision { get; }
        bool IsDirty { get; }
        object LoadToken { get; }
        byte[] CaptureBytes(MessagePackSerializerOptions options);
        void Acknowledge(long revision);
    }

    /// <summary>
    /// Data owns its state and revision. A typed Domain owner calls the derived Data's
    /// OwnerOnly mutation methods; those methods call MarkDirty only when state changes.
    /// CaptureSnapshot must return detached values, never services or live Unity objects.
    /// </summary>
    public abstract class UserDataUnit<TSnapshot> : IDataLayer, ILocalSaveUnit
    {
        protected UserDataUnit(string key, int schemaVersion, SaveLoadResult<TSnapshot> loaded)
        {
            SaveKey.Validate(key);
            if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            Key = key;
            SchemaVersion = schemaVersion;
            Revision = loaded.Revision;
            PersistedRevision = loaded.RequiresSave ? loaded.Revision - 1 : loaded.Revision;
            LoadToken = loaded.Token;
        }

        public string Key { get; }
        public int SchemaVersion { get; }
        public long Revision { get; private set; }
        public long PersistedRevision { get; private set; }
        public bool IsDirty => Revision != PersistedRevision;
        private object LoadToken { get; }
        object ILocalSaveUnit.LoadToken => LoadToken;

        protected void MarkDirty()
        {
            Revision = checked(Revision + 1);
        }

        public abstract TSnapshot CaptureSnapshot();

        byte[] ILocalSaveUnit.CaptureBytes(MessagePackSerializerOptions options)
        {
            TSnapshot snapshot = CaptureSnapshot();
            if (ReferenceEquals(snapshot, null))
                throw new InvalidOperationException($"Save unit '{Key}' returned a null snapshot.");
            return MessagePackSerializer.Serialize(snapshot, options);
        }

        void ILocalSaveUnit.Acknowledge(long revision)
        {
            if (revision < 1 || revision > Revision)
                throw new ArgumentOutOfRangeException(nameof(revision));
            PersistedRevision = Math.Max(PersistedRevision, revision);
        }
    }
}
