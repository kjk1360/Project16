using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Kylin.DI;
using Kylin.Serialization.MessagePack;
using MessagePack;

namespace Project16.Foundation.Persistence
{
    /// <summary>
    /// Composition-owned local persistence infrastructure, not a business service locator.
    /// Load before building the Data graph, then track its units. Use one live session for
    /// each account directory, and call this session only from its creating thread.
    /// The owner flushes before disposing the Data graph and owns this object's Dispose.
    /// </summary>
    public sealed class LocalSaveSession : IDependencyObject, IDisposable
    {
        private sealed class LoadedKey
        {
            public readonly object Token = new object();
            public int SchemaVersion;
            public bool PreserveBackup;
        }

        private readonly ILocalSaveStore _store;
        private readonly MessagePackSerializerOptions _options;
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<string, LoadedKey> _loaded = new Dictionary<string, LoadedKey>();
        private readonly List<ILocalSaveUnit> _units = new List<ILocalSaveUnit>();
        private readonly HashSet<string> _trackedKeys = new HashSet<string>();
        private bool _flushing;
        private bool _disposed;

        public LocalSaveSession(string storageRoot, MessagePackSerializerOptions options = null)
            : this(new LocalFileSaveStore(storageRoot), options) { }

        public LocalSaveSession(ILocalSaveStore store, MessagePackSerializerOptions options = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _options = (options ?? MessagePackSerializerOptions.Standard
                .WithSecurity(MessagePackSecurity.UntrustedData)).WithKDI();
            LastReport = new SaveReport(0, 0, new List<SaveFailure>());
        }

        public MessagePackSerializerOptions SerializerOptions => _options;
        public SaveReport LastReport { get; private set; }

        public bool IsDirty
        {
            get
            {
                CheckThread();
                foreach (ILocalSaveUnit unit in _units)
                    if (unit.IsDirty) return true;
                return false;
            }
        }

        public SaveLoadResult<T> Load<T>(string key, int currentSchema, Func<T> createDefault,
            Func<int, byte[], MessagePackSerializerOptions, T> migration = null)
        {
            CheckUsable();
            if (_flushing) throw new InvalidOperationException("Cannot load a save unit during a flush.");
            SaveKey.Validate(key);
            if (currentSchema < 1) throw new ArgumentOutOfRangeException(nameof(currentSchema));
            if (createDefault == null) throw new ArgumentNullException(nameof(createDefault));
            if (_loaded.ContainsKey(key)) throw new InvalidOperationException($"Save unit '{key}' was already loaded in this session.");

            var identity = new LoadedKey { SchemaVersion = currentSchema };
            Exception primaryCorruption = null;
            SaveLoadResult<T> result;
            try
            {
                byte[] bytes = Read(key, false);
                if (bytes != null)
                {
                    result = Decode(key, bytes, currentSchema, migration, false, identity.Token);
                    _loaded.Add(key, identity);
                    return result;
                }
            }
            catch (LocalSaveLoadException exception) when (exception.Failure == SaveLoadFailure.CorruptData)
            {
                primaryCorruption = exception;
            }

            try
            {
                byte[] backup = Read(key, true);
                if (backup != null)
                {
                    result = Decode(key, backup, currentSchema, migration, true, identity.Token);
                    identity.PreserveBackup = true;
                    _loaded.Add(key, identity);
                    return result;
                }
            }
            catch (LocalSaveLoadException exception) when (exception.Failure == SaveLoadFailure.CorruptData)
            {
                throw new LocalSaveLoadException(key, SaveLoadFailure.CorruptData,
                    $"No valid save or backup could be loaded for '{key}'. Existing files were preserved.",
                    primaryCorruption == null ? exception : new AggregateException(primaryCorruption, exception));
            }

            if (primaryCorruption != null)
                throw new LocalSaveLoadException(key, SaveLoadFailure.CorruptData,
                    $"Save '{key}' is corrupt and has no valid backup. Existing files were preserved.", primaryCorruption);

            T initial = createDefault();
            if (ReferenceEquals(initial, null)) throw new InvalidOperationException($"Default snapshot for '{key}' is null.");
            result = new SaveLoadResult<T>(initial, 1, true, false, identity.Token);
            _loaded.Add(key, identity);
            return result;
        }

        public void Track<T>(UserDataUnit<T> unit)
        {
            CheckUsable();
            if (_flushing) throw new InvalidOperationException("Cannot track a save unit during a flush.");
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            var tracked = (ILocalSaveUnit)unit;
            if (!_loaded.TryGetValue(unit.Key, out LoadedKey loaded) ||
                !ReferenceEquals(loaded.Token, tracked.LoadToken) || loaded.SchemaVersion != unit.SchemaVersion)
                throw new InvalidOperationException("Track requires a unit constructed from this session's matching Load result.");
            if (!_trackedKeys.Add(unit.Key)) throw new InvalidOperationException($"Save unit '{unit.Key}' is already tracked.");
            _units.Add(tracked);
        }

        public SaveReport FlushDirty()
        {
            CheckUsable();
            if (_flushing) return new SaveReport(0, 0, new List<SaveFailure>(), true);
            _flushing = true;
            int attempted = 0;
            int saved = 0;
            var failures = new List<SaveFailure>();
            try
            {
                foreach (ILocalSaveUnit unit in _units)
                {
                    if (!unit.IsDirty) continue;
                    attempted++;
                    try
                    {
                        long revision = unit.Revision;
                        byte[] payload = unit.CaptureBytes(_options);
                        byte[] bytes = SaveEnvelopeCodec.Encode(unit.Key, unit.SchemaVersion, revision, payload);
                        LoadedKey loaded = _loaded[unit.Key];
                        _store.Write(unit.Key, bytes, loaded.PreserveBackup);
                        unit.Acknowledge(revision);
                        loaded.PreserveBackup = false;
                        saved++;
                    }
                    catch (Exception exception)
                    {
                        failures.Add(new SaveFailure(unit.Key, exception));
                    }
                }
                LastReport = new SaveReport(attempted, saved, failures);
                return LastReport;
            }
            finally
            {
                _flushing = false;
            }
        }

        public void Dispose()
        {
            CheckThread();
            if (_disposed) return;
            if (_flushing) throw new InvalidOperationException("Cannot dispose a save session during a flush.");
            LastReport = FlushDirty();
            Abandon();
        }

        /// <summary>
        /// Detaches without serializing. Use after an explicit final flush, or when graph
        /// construction failed and freshly created defaults must not be committed.
        /// </summary>
        public void Abandon()
        {
            CheckThread();
            if (_disposed) return;
            if (_flushing) throw new InvalidOperationException("Cannot abandon a save session during a flush.");
            _disposed = true;
            _units.Clear();
            _trackedKeys.Clear();
            _loaded.Clear();
        }

        private byte[] Read(string key, bool backup)
        {
            try { return backup ? _store.ReadBackup(key) : _store.ReadPrimary(key); }
            catch (Exception exception)
            {
                var failure = exception is InvalidDataException || exception is EndOfStreamException
                    ? SaveLoadFailure.CorruptData : SaveLoadFailure.ReadFailed;
                throw new LocalSaveLoadException(key, failure,
                    $"Could not read {(backup ? "backup" : "save")} '{key}'. Existing files were preserved.", exception);
            }
        }

        private SaveLoadResult<T> Decode<T>(string key, byte[] bytes, int currentSchema,
            Func<int, byte[], MessagePackSerializerOptions, T> migration, bool recovered, object token)
        {
            SaveEnvelope envelope;
            try { envelope = SaveEnvelopeCodec.Decode(key, bytes); }
            catch (UnsupportedSaveFormatException exception)
            {
                throw new LocalSaveLoadException(key, SaveLoadFailure.UnsupportedVersion, exception.Message, exception);
            }
            catch (Exception exception)
            {
                throw new LocalSaveLoadException(key, SaveLoadFailure.CorruptData, $"Invalid save envelope for '{key}'.", exception);
            }

            if (envelope.SchemaVersion > currentSchema || (envelope.SchemaVersion < currentSchema && migration == null))
                throw new LocalSaveLoadException(key, SaveLoadFailure.UnsupportedVersion,
                    $"Save '{key}' uses schema {envelope.SchemaVersion}; this unit supports schema {currentSchema}. Files were preserved.");

            bool migrate = envelope.SchemaVersion < currentSchema;
            try
            {
                // A payload represents exactly one snapshot, including when a migration reads it.
                var payloadReader = new MessagePackReader(new ReadOnlyMemory<byte>(envelope.Payload));
                payloadReader.Skip();
                if (!payloadReader.End) throw new InvalidDataException("Save snapshot contains trailing MessagePack values.");
                T snapshot = migrate
                    ? migration(envelope.SchemaVersion, envelope.Payload, _options)
                    : MessagePackSerializer.Deserialize<T>(envelope.Payload, _options);
                if (ReferenceEquals(snapshot, null)) throw new InvalidDataException("Save snapshot is null.");
                long revision = migrate ? checked(envelope.Revision + 1) : envelope.Revision;
                return new SaveLoadResult<T>(snapshot, revision, recovered || migrate, recovered, token);
            }
            catch (Exception exception)
            {
                throw new LocalSaveLoadException(key, migrate ? SaveLoadFailure.MigrationFailed : SaveLoadFailure.CorruptData,
                    $"Could not {(migrate ? "migrate" : "deserialize")} save '{key}'. Existing files were preserved.", exception);
            }
        }

        private void CheckUsable()
        {
            CheckThread();
            if (_disposed) throw new ObjectDisposedException(nameof(LocalSaveSession));
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
                throw new InvalidOperationException("LocalSaveSession must be used on its creating thread.");
        }
    }
}
