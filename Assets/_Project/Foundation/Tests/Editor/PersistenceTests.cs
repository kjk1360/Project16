using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Kylin.Serialization.MessagePack;
using Kylin.SubscribableProperty;
using MessagePack;
using MessagePack.Formatters;
using NUnit.Framework;
using Project16.Foundation.Persistence;

namespace Project16.Foundation.Tests
{
    public sealed class PersistenceTests
    {
        private string _root;
        private LocalFileSaveStore _files;
        private readonly List<LocalSaveSession> _sessions = new List<LocalSaveSession>();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Project16-PersistenceTests-" + Guid.NewGuid().ToString("N"));
            _files = new LocalFileSaveStore(_root);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (LocalSaveSession session in _sessions) session.Abandon();
            _sessions.Clear();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void Flush_WritesOnlyDirtyUnits_AndCleanFlushPerformsNoIo()
        {
            var store = new RecordingStore(_files);
            LocalSaveSession session = Session(store);
            IntUnit first = LoadInt(session, "first", 3);
            LoadInt(session, "second", 7);
            Assert.That(Directory.Exists(_root), Is.False, "Loading defaults must not write files.");

            Assert.That(session.FlushDirty().SavedCount, Is.EqualTo(2));
            byte[] unchanged = File.ReadAllBytes(_files.GetPrimaryPath("second"));
            store.Writes.Clear();
            Assert.That(session.FlushDirty().AttemptedCount, Is.Zero);
            Assert.That(store.Writes, Is.Empty);

            first.Change(4);
            SaveReport report = session.FlushDirty();
            Assert.That(report.Succeeded, Is.True);
            Assert.That(report.SavedCount, Is.EqualTo(1));
            Assert.That(store.Writes, Is.EqualTo(new[] { "first" }));
            Assert.That(File.ReadAllBytes(_files.GetPrimaryPath("second")), Is.EqualTo(unchanged));
            Assert.That(session.IsDirty, Is.False);
        }

        [Test]
        public void Load_RoundTripsACommittedUnit_WithoutMarkingItDirty()
        {
            LocalSaveSession writer = Session();
            IntUnit original = LoadInt(writer, "preferences", 10);
            original.Change(20);
            Assert.That(writer.FlushDirty().Succeeded, Is.True);
            long revision = original.Revision;
            writer.Abandon();

            LocalSaveSession reader = Session();
            SaveLoadResult<int> loaded = reader.Load("preferences", 1, () => -1);
            var restored = new IntUnit("preferences", 1, loaded);
            reader.Track(restored);
            Assert.That(loaded.Snapshot, Is.EqualTo(20));
            Assert.That(loaded.RequiresSave, Is.False);
            Assert.That(loaded.RecoveredFromBackup, Is.False);
            Assert.That(restored.PersistedRevision, Is.EqualTo(revision));
            Assert.That(reader.FlushDirty().AttemptedCount, Is.Zero);
        }

        [Test]
        public void CorruptPrimary_RecoversBackup_AndRepairPreservesTheValidBackup()
        {
            SeedTwoVersions();
            byte[] backup = File.ReadAllBytes(_files.GetBackupPath("state"));
            File.WriteAllBytes(_files.GetPrimaryPath("state"), new byte[] { 0xc1, 0xff });

            LocalSaveSession session = Session();
            SaveLoadResult<int> loaded = session.Load("state", 1, () => -1);
            Assert.That(loaded.Snapshot, Is.EqualTo(10));
            Assert.That(loaded.RecoveredFromBackup, Is.True);
            Assert.That(loaded.RequiresSave, Is.True);
            var unit = new IntUnit("state", 1, loaded);
            session.Track(unit);
            Assert.That(session.FlushDirty().Succeeded, Is.True);
            Assert.That(File.ReadAllBytes(_files.GetBackupPath("state")), Is.EqualTo(backup));
            unit.Change(30);
            Assert.That(session.FlushDirty().Succeeded, Is.True);
            Assert.That(File.ReadAllBytes(_files.GetBackupPath("state")), Is.EqualTo(backup));
            session.Abandon();
            Assert.That(Session().Load("state", 1, () => -1).Snapshot, Is.EqualTo(30));
        }

        [Test]
        public void MissingPrimary_RecoversAnExistingBackup()
        {
            SeedTwoVersions();
            File.Delete(_files.GetPrimaryPath("state"));
            SaveLoadResult<int> loaded = Session().Load("state", 1, () => -1);
            Assert.That(loaded.Snapshot, Is.EqualTo(10));
            Assert.That(loaded.RecoveredFromBackup, Is.True);
        }

        [Test]
        public void CorruptPrimaryAndBackup_FailClosed_WithoutCreatingDefaultsOrChangingFiles()
        {
            Directory.CreateDirectory(_root);
            byte[] primary = { 0xc1, 1 };
            byte[] backup = { 0xc1, 2 };
            File.WriteAllBytes(_files.GetPrimaryPath("state"), primary);
            File.WriteAllBytes(_files.GetBackupPath("state"), backup);
            bool createdDefault = false;
            LocalSaveSession session = Session();
            var error = Assert.Throws<LocalSaveLoadException>(() => session.Load("state", 1, () =>
            {
                createdDefault = true;
                return 0;
            }));
            Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.CorruptData));
            Assert.That(createdDefault, Is.False);
            Assert.That(session.FlushDirty().AttemptedCount, Is.Zero);
            Assert.That(File.ReadAllBytes(_files.GetPrimaryPath("state")), Is.EqualTo(primary));
            Assert.That(File.ReadAllBytes(_files.GetBackupPath("state")), Is.EqualTo(backup));
        }

        [Test]
        public void CorruptPrimaryWithoutBackup_FailsClosed()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllBytes(_files.GetPrimaryPath("state"), new byte[] { 0xc1 });
            var error = Assert.Throws<LocalSaveLoadException>(() => Session().Load("state", 1, () => 0));
            Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.CorruptData));
        }

        [Test]
        public void FutureSchema_DoesNotFallBackToOlderBackup_OrOverwriteFutureSave()
        {
            SeedTwoVersions();
            LocalSaveSession newer = Session();
            SaveLoadResult<int> upgraded = newer.Load("state", 2, () => 0,
                (version, payload, options) => MessagePackSerializer.Deserialize<int>(payload, options));
            var current = new IntUnit("state", 2, upgraded);
            newer.Track(current);
            current.Change(99);
            newer.FlushDirty();
            newer.Abandon();
            byte[] future = File.ReadAllBytes(_files.GetPrimaryPath("state"));
            byte[] backup = File.ReadAllBytes(_files.GetBackupPath("state"));

            LocalSaveSession older = Session();
            var error = Assert.Throws<LocalSaveLoadException>(() => older.Load("state", 1, () => -1));
            Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.UnsupportedVersion));
            older.Dispose();
            Assert.That(File.ReadAllBytes(_files.GetPrimaryPath("state")), Is.EqualTo(future));
            Assert.That(File.ReadAllBytes(_files.GetBackupPath("state")), Is.EqualTo(backup));
        }

        [Test]
        public void Migration_UsesTheOldPayload_AndCommitsOnlyOnExplicitFlush()
        {
            LocalSaveSession old = Session();
            LoadInt(old, "state", 42);
            old.FlushDirty();
            old.Abandon();
            byte[] before = File.ReadAllBytes(_files.GetPrimaryPath("state"));

            LocalSaveSession current = Session();
            int observedVersion = 0;
            SaveLoadResult<string> loaded = current.Load("state", 2, () => "default",
                (version, bytes, options) =>
                {
                    observedVersion = version;
                    return "value:" + MessagePackSerializer.Deserialize<int>(bytes, options);
                });
            Assert.That(observedVersion, Is.EqualTo(1));
            Assert.That(loaded.Snapshot, Is.EqualTo("value:42"));
            Assert.That(loaded.RequiresSave, Is.True);
            Assert.That(File.ReadAllBytes(_files.GetPrimaryPath("state")), Is.EqualTo(before));
            var unit = new StringUnit("state", 2, loaded);
            current.Track(unit);
            Assert.That(current.FlushDirty().SavedCount, Is.EqualTo(1));
            current.Abandon();
            Assert.That(Session().Load("state", 2, () => "default").Snapshot, Is.EqualTo("value:42"));
        }

        [Test]
        public void MissingMigration_FailsClosed()
        {
            SeedTwoVersions();
            var error = Assert.Throws<LocalSaveLoadException>(() => Session().Load("state", 2, () => 0));
            Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.UnsupportedVersion));
        }

        [Test]
        public void FailedMigration_PreservesTheOriginalData()
        {
            SeedTwoVersions();
            byte[] before = File.ReadAllBytes(_files.GetPrimaryPath("state"));
            var error = Assert.Throws<LocalSaveLoadException>(() => Session().Load<int>("state", 2, () => 0,
                (version, bytes, options) => throw new InvalidOperationException("No migration path.")));
            Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.MigrationFailed));
            Assert.That(File.ReadAllBytes(_files.GetPrimaryPath("state")), Is.EqualTo(before));
        }

        [Test]
        public void WriteFailure_RemainsDirty_DoesNotBlockOtherUnits_AndCanRetry()
        {
            var store = new RecordingStore(_files) { FailingKey = "first" };
            LocalSaveSession session = Session(store);
            IntUnit first = LoadInt(session, "first", 11);
            IntUnit second = LoadInt(session, "second", 22);

            SaveReport failed = session.FlushDirty();
            Assert.That(failed.AttemptedCount, Is.EqualTo(2));
            Assert.That(failed.SavedCount, Is.EqualTo(1));
            Assert.That(failed.Failures.Count, Is.EqualTo(1));
            Assert.That(failed.Failures[0].Key, Is.EqualTo("first"));
            Assert.That(first.IsDirty, Is.True);
            Assert.That(second.IsDirty, Is.False);
            Assert.That(File.Exists(_files.GetPrimaryPath("first")), Is.False);

            store.FailingKey = null;
            store.Writes.Clear();
            Assert.That(session.FlushDirty().Succeeded, Is.True);
            Assert.That(store.Writes, Is.EqualTo(new[] { "first" }));
            Assert.That(session.IsDirty, Is.False);
        }

        [Test]
        public void MutationDuringCommit_DoesNotAcknowledgeTheNewerRevision()
        {
            var store = new RecordingStore(_files);
            LocalSaveSession session = Session(store);
            IntUnit unit = LoadInt(session, "state", 1);
            long capturedRevision = unit.Revision;
            store.AfterCommit = () => unit.Change(2);

            Assert.That(session.FlushDirty().Succeeded, Is.True);
            Assert.That(unit.PersistedRevision, Is.EqualTo(capturedRevision));
            Assert.That(unit.Revision, Is.GreaterThan(capturedRevision));
            Assert.That(unit.IsDirty, Is.True);
            store.AfterCommit = null;
            Assert.That(session.FlushDirty().SavedCount, Is.EqualTo(1));
            Assert.That(unit.IsDirty, Is.False);
            session.Abandon();
            Assert.That(Session().Load("state", 1, () => 0).Snapshot, Is.EqualTo(2));
        }

        [Test]
        public void ReentrantFlush_ReturnsBusy_WithoutRepeatingWrites()
        {
            var store = new RecordingStore(_files);
            LocalSaveSession session = Session(store);
            LoadInt(session, "state", 1);
            SaveReport nested = null;
            store.AfterCommit = () => nested = session.FlushDirty();
            SaveReport outer = session.FlushDirty();
            Assert.That(nested.IsBusy, Is.True);
            Assert.That(nested.Succeeded, Is.False);
            Assert.That(outer.Succeeded, Is.True);
            Assert.That(store.Writes.Count, Is.EqualTo(1));
            Assert.That(session.LastReport, Is.SameAs(outer));
        }

        [Test]
        public void CaptureFailure_RemainsDirty_AndOtherUnitsStillCommit()
        {
            LocalSaveSession session = Session();
            var bad = new ThrowingUnit("bad", session.Load("bad", 1, () => 1));
            session.Track(bad);
            LoadInt(session, "good", 2);
            SaveReport report = session.FlushDirty();
            Assert.That(report.SavedCount, Is.EqualTo(1));
            Assert.That(report.Failures[0].Key, Is.EqualTo("bad"));
            Assert.That(bad.IsDirty, Is.True);
            Assert.That(File.Exists(_files.GetPrimaryPath("bad")), Is.False);
        }

        [Test]
        public void AtomicReplacementFailure_PreservesDirty_AndRemovesTemporaryFile()
        {
            Directory.CreateDirectory(_files.GetPrimaryPath("state"));
            var store = new RecordingStore(_files) { HidePrimaryRead = true };
            LocalSaveSession session = Session(store);
            IntUnit unit = LoadInt(session, "state", 3);
            SaveReport report = session.FlushDirty();
            Assert.That(report.Succeeded, Is.False);
            Assert.That(unit.IsDirty, Is.True);
            Assert.That(Directory.Exists(_files.GetPrimaryPath("state")), Is.True);
            Assert.That(File.Exists(_files.GetPrimaryPath("state") + ".tmp"), Is.False);
        }

        [Test]
        public void ReadFailure_DoesNotCreateDefaults_OrTreatFailureAsMissing()
        {
            var store = new RecordingStore(_files) { FailRead = true };
            LocalSaveSession session = Session(store);
            bool defaultCalled = false;
            var error = Assert.Throws<LocalSaveLoadException>(() => session.Load("state", 1, () =>
            {
                defaultCalled = true;
                return 1;
            }));
            Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.ReadFailed));
            Assert.That(defaultCalled, Is.False);
            Assert.That(store.Writes, Is.Empty);
        }

        [Test]
        public void Abandon_DoesNotWriteDefaults_AndIsIdempotent()
        {
            LocalSaveSession session = Session();
            LoadInt(session, "state", 1);
            session.Abandon();
            session.Abandon();
            session.Dispose();
            Assert.That(Directory.Exists(_root), Is.False);
            Assert.Throws<ObjectDisposedException>(() => session.FlushDirty());
        }

        [Test]
        public void Dispose_PerformsOneFinalDirtyFlush_AndKeepsItsReport()
        {
            var store = new RecordingStore(_files);
            LocalSaveSession session = Session(store);
            LoadInt(session, "state", 1);
            session.Dispose();
            session.Dispose();
            Assert.That(store.Writes.Count, Is.EqualTo(1));
            Assert.That(session.LastReport.SavedCount, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateOrForeignUnit_CannotTakeOwnershipOfALoadedKey()
        {
            LocalSaveSession first = Session();
            IntUnit unit = LoadInt(first, "state", 1);
            Assert.Throws<InvalidOperationException>(() => first.Track(unit));
            Assert.Throws<InvalidOperationException>(() => first.Load("state", 1, () => 0));
            LocalSaveSession second = Session();
            second.Load("state", 1, () => 2);
            Assert.Throws<InvalidOperationException>(() => second.Track(unit));
        }

        [TestCase("../state")]
        [TestCase("State")]
        [TestCase("state/name")]
        [TestCase("state.name")]
        [TestCase("")]
        [TestCase("1state")]
        [TestCase("con")]
        [TestCase("prn")]
        [TestCase("aux")]
        [TestCase("nul")]
        [TestCase("com1")]
        [TestCase("com9")]
        [TestCase("lpt1")]
        [TestCase("lpt9")]
        public void InvalidKey_IsRejectedBeforeFilesystemAccess(string key)
        {
            Assert.Throws<ArgumentException>(() => Session().Load(key, 1, () => 0));
            Assert.That(Directory.Exists(_root), Is.False);
        }

        [Test]
        public void Session_RejectsUseFromAnotherThread()
        {
            LocalSaveSession session = Session();
            Exception error = Task.Run(() =>
            {
                try { session.FlushDirty(); return null; }
                catch (Exception exception) { return exception; }
            }).GetAwaiter().GetResult();
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Options_IncludeKdiFormatters_WithoutChangingGlobalOptions()
        {
            MessagePackSerializerOptions global = MessagePackSerializer.DefaultOptions;
            KDIMessagePackAot.RegisterProperty<int>();
            LocalSaveSession writer = Session();
            var loaded = writer.Load("property", 1, () => new SubscribableProperty<int>(8));
            var unit = new PropertyUnit(loaded);
            writer.Track(unit);
            Assert.That(writer.FlushDirty().Succeeded, Is.True);
            writer.Abandon();
            Assert.That(Session().Load("property", 1, () => new SubscribableProperty<int>(0)).Snapshot.Value,
                Is.EqualTo(8));
            Assert.That(MessagePackSerializer.DefaultOptions, Is.SameAs(global));
        }

        [Test]
        public void UncommittedTemporaryFile_IsIgnoredOnLoad()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllBytes(_files.GetPrimaryPath("state") + ".tmp", new byte[] { 0xc1 });
            var loaded = Session().Load("state", 1, () => 5);
            Assert.That(loaded.Snapshot, Is.EqualTo(5));
            Assert.That(loaded.RequiresSave, Is.True);
            Assert.That(File.Exists(_files.GetPrimaryPath("state")), Is.False);
        }

        [Test]
        public void PayloadWithTrailingValues_IsRejectedEvenWhenEnvelopeChecksumIsValid()
        {
            LocalSaveSession writer = Session();
            var loaded = writer.Load("trailing", 1, () => new TrailingSnapshot());
            writer.Track(new TrailingUnit(loaded));
            Assert.That(writer.FlushDirty().Succeeded, Is.True);
            writer.Abandon();
            var error = Assert.Throws<LocalSaveLoadException>(() =>
                Session().Load("trailing", 1, () => new TrailingSnapshot()));
            Assert.That(error.Failure, Is.EqualTo(SaveLoadFailure.CorruptData));
        }

        private LocalSaveSession Session(ILocalSaveStore store = null)
        {
            var session = new LocalSaveSession(store ?? _files);
            _sessions.Add(session);
            return session;
        }

        private static IntUnit LoadInt(LocalSaveSession session, string key, int initial)
        {
            var unit = new IntUnit(key, 1, session.Load(key, 1, () => initial));
            session.Track(unit);
            return unit;
        }

        private void SeedTwoVersions()
        {
            LocalSaveSession session = Session();
            IntUnit unit = LoadInt(session, "state", 10);
            Assert.That(session.FlushDirty().Succeeded, Is.True);
            unit.Change(20);
            Assert.That(session.FlushDirty().Succeeded, Is.True);
            session.Abandon();
        }

        private sealed class IntUnit : UserDataUnit<int>
        {
            private int _value;

            public IntUnit(string key, int schema, SaveLoadResult<int> loaded) : base(key, schema, loaded)
            {
                _value = loaded.Snapshot;
            }

            public void Change(int value)
            {
                if (_value == value) return;
                _value = value;
                MarkDirty();
            }

            public override int CaptureSnapshot() => _value;
        }

        private sealed class StringUnit : UserDataUnit<string>
        {
            private readonly string _value;

            public StringUnit(string key, int schema, SaveLoadResult<string> loaded) : base(key, schema, loaded)
            {
                _value = loaded.Snapshot;
            }

            public override string CaptureSnapshot() => _value;
        }

        private sealed class ThrowingUnit : UserDataUnit<int>
        {
            public ThrowingUnit(string key, SaveLoadResult<int> loaded) : base(key, 1, loaded) { }
            public override int CaptureSnapshot() => throw new InvalidOperationException("Snapshot failed.");
        }

        private sealed class PropertyUnit : UserDataUnit<SubscribableProperty<int>>
        {
            private readonly int _value;
            public PropertyUnit(SaveLoadResult<SubscribableProperty<int>> loaded) : base("property", 1, loaded)
            {
                _value = loaded.Snapshot.Value;
            }

            public override SubscribableProperty<int> CaptureSnapshot() => new SubscribableProperty<int>(_value);
        }

        private sealed class RecordingStore : ILocalSaveStore
        {
            private readonly ILocalSaveStore _inner;
            public readonly List<string> Writes = new List<string>();
            public string FailingKey;
            public Action AfterCommit;
            public bool FailRead;
            public bool HidePrimaryRead;

            public RecordingStore(ILocalSaveStore inner) { _inner = inner; }
            public byte[] ReadPrimary(string key)
            {
                if (FailRead) throw new IOException("Read denied.");
                return HidePrimaryRead ? null : _inner.ReadPrimary(key);
            }

            public byte[] ReadBackup(string key) => _inner.ReadBackup(key);

            public void Write(string key, byte[] envelope, bool preserveBackup)
            {
                Writes.Add(key);
                if (key == FailingKey) throw new IOException("Storage is unavailable.");
                _inner.Write(key, envelope, preserveBackup);
                AfterCommit?.Invoke();
            }
        }

        private sealed class TrailingUnit : UserDataUnit<TrailingSnapshot>
        {
            public TrailingUnit(SaveLoadResult<TrailingSnapshot> loaded) : base("trailing", 1, loaded) { }
            public override TrailingSnapshot CaptureSnapshot() => new TrailingSnapshot();
        }

        [MessagePackFormatter(typeof(TrailingSnapshotFormatter))]
        public sealed class TrailingSnapshot { }

        public sealed class TrailingSnapshotFormatter : IMessagePackFormatter<TrailingSnapshot>
        {
            public void Serialize(ref MessagePackWriter writer, TrailingSnapshot value, MessagePackSerializerOptions options)
            {
                writer.Write(1);
                writer.Write(2);
            }

            public TrailingSnapshot Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            {
                reader.ReadInt32();
                return new TrailingSnapshot();
            }
        }
    }
}
