using System;
using System.Collections.Generic;
using BansheeGz.BGDatabase;
using NUnit.Framework;
using Project16.Foundation.BGDatabase;
using Project16.Foundation.Specs;

namespace Project16.Foundation.Tests
{
    public sealed class SpecTests
    {
        private BGRepo _authored;

        [SetUp]
        public void SetUp() => _authored = new BGRepo();

        [TearDown]
        public void TearDown() => _authored.Clear();

        [Test]
        public void PrivateRepositoryMapsRelationsWithoutLoadingOrChangingTheGlobalRepository()
        {
            var wasLoaded = BGRepo.DefaultRepoLoaded;
            var loadError = BGRepo.DefaultRepoErrorOnLoad;
            var assetPath = BGRepo.DefaultRepoAssetPath;
            CreateLookupAndReference();

            using var source = BgSpecSource.Load(_authored.Save());
            var lookup = source.ReadTable("lookup", row => new LookupSpec(row.Name, row.ReadInt("value")));
            var references = source.ReadTable("references", row =>
                new ReferenceSpec(row.Name, row.ReadRequiredRelationKey("target", "lookup")));

            Assert.That(lookup.Get("alpha").Value, Is.EqualTo(7));
            Assert.That(references.Get("first").TargetKey, Is.EqualTo("alpha"));
            Assert.That(BGRepo.DefaultRepoLoaded, Is.EqualTo(wasLoaded));
            Assert.That(BGRepo.DefaultRepoErrorOnLoad, Is.EqualTo(loadError));
            Assert.That(BGRepo.DefaultRepoAssetPath, Is.EqualTo(assetPath));
        }

        [Test]
        public void SnapshotSurvivesProviderChangesAndSourceDisposal()
        {
            CreateLookupAndReference();
            var source = BgSpecSource.Load(_authored.Save());
            BgSpecRowReader retainedReader = null;
            var snapshot = source.ReadTable("lookup", row =>
            {
                retainedReader = row;
                return new LookupSpec(row.Name, row.ReadInt("value"));
            });

            _authored.GetMeta("lookup").GetEntity("alpha").Set("value", 99);
            source.Dispose();
            source.Dispose();
            _authored.Clear();

            Assert.That(snapshot.Get("alpha").Value, Is.EqualTo(7));
            Assert.Throws<ObjectDisposedException>(() => retainedReader.ReadInt("value"));
            Assert.Throws<ObjectDisposedException>(() => source.ReadTable("lookup", row => new LookupSpec(row.Name, 0)));
        }

        [Test]
        public void RequiredRelationWithoutATargetRejectsTheSnapshot()
        {
            CreateLookupAndReference();
            _authored.GetMeta("references").GetEntity("first").Set<BGEntity>("target", null);
            using var source = BgSpecSource.Load(_authored.Save());

            var error = Assert.Throws<SpecValidationException>(() => source.ReadTable("references", row =>
                new ReferenceSpec(row.Name, row.ReadRequiredRelationKey("target", "lookup"))));

            StringAssert.Contains("target", error.Message);
            StringAssert.Contains("missing", error.Message);
        }

        [Test]
        public void RelationToAnUnexpectedTableRejectsTheSnapshot()
        {
            CreateLookupAndReference();
            using var source = BgSpecSource.Load(_authored.Save());

            var error = Assert.Throws<SpecValidationException>(() => source.ReadTable("references", row =>
                new ReferenceSpec(row.Name, row.ReadRequiredRelationKey("target", "differentTable"))));

            StringAssert.Contains("differentTable", error.Message);
        }

        [Test]
        public void DuplicateAuthoredKeysRejectTheWholeTable()
        {
            var table = new BGMetaRow(_authored, "lookup");
            new BGFieldString(table, "key");
            table.NewEntity().Set("key", "same");
            table.NewEntity().Set("key", "same");
            using var source = BgSpecSource.Load(_authored.Save());

            var error = Assert.Throws<SpecValidationException>(() => source.ReadTable("lookup", row =>
                new LookupSpec(row.ReadString("key"), 0)));

            StringAssert.Contains("duplicate", error.Message);
        }

        [Test]
        public void MissingTableOrWrongFieldTypeReportsSchemaFailure()
        {
            CreateLookupAndReference();
            using var source = BgSpecSource.Load(_authored.Save());

            Assert.Throws<SpecValidationException>(() => source.ReadTable("absent", row => new LookupSpec(row.Name, 0)));
            var error = Assert.Throws<SpecValidationException>(() => source.ReadTable("lookup", row =>
                new ReferenceSpec(row.Name, row.ReadString("value"))));
            StringAssert.Contains("lookup", error.Message);
            StringAssert.Contains("value", error.Message);
        }

        [Test]
        public void TableCopiesTheSuppliedValuesAndDoesNotExposeAWritableList()
        {
            var rows = new[] { new LookupSpec("alpha", 7) };
            var table = new SpecTable<LookupSpec>(rows);
            rows[0] = new LookupSpec("changed", 99);

            Assert.That(table.Get("alpha").Value, Is.EqualTo(7));
            Assert.That(table.TryGet("changed", out _), Is.False);
            Assert.That(table.TryGet(null, out _), Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<LookupSpec>)table.Rows)[0] = rows[0]);
        }

        [Test]
        public void EmptyOrTruncatedContentDoesNotBecomeAnEmptySpecificationDatabase()
        {
            Assert.Throws<SpecValidationException>(() => BgSpecSource.Load(Array.Empty<byte>()));
            Assert.Throws<SpecValidationException>(() => BgSpecSource.Load(new byte[4]));
        }

        private void CreateLookupAndReference()
        {
            var lookup = new BGMetaRow(_authored, "lookup");
            new BGFieldInt(lookup, "value");
            var target = lookup.NewEntity();
            target.Name = "alpha";
            target.Set("value", 7);

            var references = new BGMetaRow(_authored, "references");
            new BGFieldRelationSingle(references, "target", lookup);
            var reference = references.NewEntity();
            reference.Name = "first";
            reference.Set("target", target);
        }

        private readonly struct LookupSpec : ISpecRow
        {
            public LookupSpec(string key, int value)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; }
            public int Value { get; }
        }

        private readonly struct ReferenceSpec : ISpecRow
        {
            public ReferenceSpec(string key, string targetKey)
            {
                Key = key;
                TargetKey = targetKey;
            }

            public string Key { get; }
            public string TargetKey { get; }
        }
    }
}
