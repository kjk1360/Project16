using System;
using System.IO;
using System.Linq;
using BansheeGz.BGDatabase;
using Kylin.DI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Project16.CardGame.Application;
using Project16.CardGame.Composition;
using Project16.CardGame.Domain;
using Project16.CardGame.Editor;
using Project16.Foundation.BGDatabase;
using Project16.Foundation.Composition;
using Project16.Foundation.Specs;
using UnityEditor;
using UnityEngine;

namespace Project16.CardGame.Tests
{
    public sealed class TablesIntegrationTests
    {
        private string _root;
        private CardGameModule _module;
        private TextAsset _asset;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "Project16-CardGameTests", Guid.NewGuid().ToString("N"));
            _module = ScriptableObject.CreateInstance<CardGameModule>();
            // TextAsset(string) does not preserve arbitrary binary bytes. Use the imported .bytes asset.
            _asset = AssetDatabase.LoadAssetAtPath<TextAsset>(GameSpecImport.OutputPath);
            Assert.That(_asset, Is.Not.Null, "Restore the tracked BG database asset or run Tools/Project16/Create Missing Card Game Specs from JSON.");
            var serialized = new SerializedObject(_module);
            serialized.FindProperty("_specDatabase").objectReferenceValue = _asset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void Teardown()
        {
            UnityEngine.Object.DestroyImmediate(_module);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void NativeBGResourceAndGameModuleUseTheSameAsset()
        {
            // Exercise BG's real resource-name lookup without loading/replacing its global repo.
            var nativeAsset = new BGLoaderForRepoResources().Load("bansheegz_database");
            Assert.That(nativeAsset, Is.SameAs(_asset));
            var assets = Resources.LoadAll<TextAsset>("bansheegz_database");
            Assert.That(assets, Has.Length.EqualTo(1), "The native BG resource must be unambiguous.");
            var module = AssetDatabase.LoadAssetAtPath<CardGameModule>("Assets/_Project/Settings/CardGameModule.asset");
            Assert.That(new SerializedObject(module).FindProperty("_specDatabase").objectReferenceValue, Is.SameAs(_asset));
            Assert.That(GameSpecLoader.Load(nativeAsset.bytes).Cards.Count, Is.GreaterThan(0));
        }

        [Test]
        public void NativeBGCellEditSurvivesSerializationAndChangesRuntimeDuration()
        {
            var repo = new BGRepoBinary().Read(_asset.bytes);
            try
            {
                repo.GetMeta("durations").GetEntity("next-own-start").Set("occurrences", 2);
                var specs = GameSpecLoader.Load(repo.Save());
                Assert.That(specs.Duration("next-own-start").Occurrences, Is.EqualTo(2));
                Assert.That(HealthAfterTwoResponses(specs), Is.EqualTo(20));
            }
            finally { repo.Clear(); }
        }

        [Test]
        public void JsonSeedCannotOverwriteExistingNativeDatabase()
        {
            var original = File.ReadAllBytes(GameSpecImport.OutputPath);
            Assert.Throws<InvalidOperationException>(() => GameSpecImport.Import());
            Assert.That(File.ReadAllBytes(GameSpecImport.OutputPath), Is.EqualTo(original));
        }

        [Test]
        public void PublicTablesLoadRuntimeRulesWithoutVerbatimSourceEvidenceOrTouchingGlobalBG()
        {
            var globalWasLoaded = BGRepo.DefaultRepoLoaded;
            var bytes = GameSpecImport.Build(File.ReadAllText(GameSpecImport.SourcePath));
            var specs = GameSpecLoader.Load(bytes);
            Assert.That(specs.Cards.Count, Is.GreaterThanOrEqualTo(16));
            Assert.That(specs.Companions.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(specs.Scenario("prototype-coop").ActorIds.Count, Is.EqualTo(2));
            Assert.That(specs.Scenario("prototype-coop").ActorIds[0], Is.EqualTo(specs.Scenario("prototype-coop").ActorIds[1]), "Repeated definitions create separate runtime instances.");
            using var source = BgSpecSource.Load(bytes);
            var evidence = source.ReadTable("sourceCards", r => new EvidenceRow(r.Name, r.ReadBool("executableReady"), r.ReadInt("page")));
            Assert.That(evidence.Count, Is.Zero, "The public sample must not redistribute verbatim source card text.");
            Assert.That(evidence.Rows.All(row => !row.Executable && row.Page >= 1 && row.Page <= 10), Is.True);
            Assert.That(BGRepo.DefaultRepoLoaded, Is.EqualTo(globalWasLoaded));
        }

        [Test]
        public void ChangingOnlyAuthoredDurationAndUpgradeDataChangesTheLoadedRuleGraph()
        {
            var document = JObject.Parse(File.ReadAllText(GameSpecImport.SourcePath));
            var duration = Table(document, "durations")["rows"].Single(row => (string)row["key"] == "next-own-start");
            duration["occurrences"] = 2;
            var loaded = GameSpecLoader.Load(GameSpecImport.Build(document.ToString()));
            Assert.That(loaded.Duration("next-own-start").Occurrences, Is.EqualTo(2));
            Assert.That(loaded.Upgrade("add-regeneration").EffectGroupId, Is.EqualTo("regenerate-group"));
            Assert.That(loaded.Upgrade("add-team-protection").EffectGroupId, Is.EqualTo("protect-party"));
        }

        [Test]
        public void ChangingOneDurationCellMakesTheSameSleepCardBlockAnExtraPersonalTurn()
        {
            var document = JObject.Parse(File.ReadAllText(GameSpecImport.SourcePath));
            var first = GameSpecLoader.Load(GameSpecImport.Build(document.ToString()));
            var duration = Table(document, "durations")["rows"].Single(row => (string)row["key"] == "next-own-start");
            duration["occurrences"] = 2;
            var second = GameSpecLoader.Load(GameSpecImport.Build(document.ToString()));
            Assert.That(HealthAfterTwoResponses(first), Is.EqualTo(18));
            Assert.That(HealthAfterTwoResponses(second), Is.EqualTo(20));
        }

        [TestCase("999")]
        [TestCase("attack")]
        public void InvalidEnumTextCannotSilentlyBecomeAnExecutableRule(string value)
        {
            var document = JObject.Parse(File.ReadAllText(GameSpecImport.SourcePath));
            Table(document, "modifiers")["rows"][0]["blockedAction"] = value;
            Assert.Throws<SpecValidationException>(() => GameSpecLoader.Load(GameSpecImport.Build(document.ToString())));
        }

        [Test]
        public void BrokenRelationOrMisspelledColumnFailsBeforeReplacingAnyAsset()
        {
            var original = File.ReadAllBytes(GameSpecImport.OutputPath);
            var document = JObject.Parse(File.ReadAllText(GameSpecImport.SourcePath));
            Table(document, "cards")["rows"][0]["companionId"] = "missing-companion";
            Assert.Throws<InvalidDataException>(() => GameSpecImport.Build(document.ToString()));
            document = JObject.Parse(File.ReadAllText(GameSpecImport.SourcePath));
            Table(document, "cards")["rows"][0]["atack"] = 50;
            Assert.Throws<InvalidDataException>(() => GameSpecImport.Build(document.ToString()));
            Assert.That(File.ReadAllBytes(GameSpecImport.OutputPath), Is.EqualTo(original));
        }

        [Test]
        public void ModuleStartsFreshRunSavesDirtyUnitAndRestoresItAcrossSessionScopes()
        {
            long revision;
            string playerId;
            using (var runtime = new GameRuntime(_root, new[] { _module }))
            {
                runtime.BeginSession("test");
                using var scene = runtime.OpenScene("rules-test");
                var application = scene.ResolveEntry<IGameApplication>();
                var data = scene.ResolveEntry<IGameData>();
                Assert.That(application.StartRun("prototype-solo", 42).Accepted, Is.True);
                playerId = data.Snapshot.CurrentPlayerId;
                Assert.That(data.Snapshot.Players[playerId].Hand.Count, Is.EqualTo(6));
                Assert.That(application.Pass(playerId).Accepted, Is.True);
                Assert.That(data.Snapshot.Battle.Step, Is.EqualTo(BattleStep.SwitchWindow));
                revision = data.Revision;
                var saved = runtime.FlushDirty();
                Assert.That(saved.Succeeded, Is.True);
                Assert.That(saved.SavedCount, Is.EqualTo(2), "preferences and card-game-run are separate units");
                Assert.That(runtime.IsDirty, Is.False);
                Assert.That(application.StartRun("prototype-solo").Accepted, Is.False, "must not overwrite an active run");
                Assert.That(data.Revision, Is.EqualTo(revision));
                Assert.That(runtime.FlushDirty().AttemptedCount, Is.Zero);
            }
            using (var runtime = new GameRuntime(_root, new[] { _module }))
            {
                runtime.BeginSession("test");
                using var scene = runtime.OpenScene("resumed");
                var data = scene.ResolveEntry<IGameData>();
                var app = scene.ResolveEntry<IGameApplication>();
                Assert.That(data.Revision, Is.EqualTo(revision));
                Assert.That(data.Snapshot.CurrentPlayerId, Is.EqualTo(playerId));
                Assert.That(data.Snapshot.Battle.Step, Is.EqualTo(BattleStep.SwitchWindow));
                Assert.That(runtime.IsDirty, Is.False);
                Assert.That(app.DeclineSwitch(playerId).Accepted, Is.True);
                Assert.That(data.Snapshot.Battle.Step, Is.EqualTo(BattleStep.PlayerAction));
                Assert.That(runtime.IsDirty, Is.True);
            }
        }

        private static JToken Table(JObject document, string name) => document["tables"].Single(table => (string)table["name"] == name);
        private static int HealthAfterTwoResponses(GameSpecCatalog specs)
        {
            GameState initial;
            using (var setup = BuildScope(specs, new GameState()))
            {
                Assert.That(setup.Resolve<IGameApplication>().StartRun("prototype-solo", 42).Accepted, Is.True);
                initial = setup.Resolve<GameData>().CaptureSnapshot();
            }
            var player = initial.Players[initial.CurrentPlayerId];
            var sleep = initial.Cards.Values.Single(card => card.OwnerId == player.Id && card.SpecId == "sleep");
            player.Deck.Remove(sleep.Id); player.Hand.Remove(sleep.Id); player.Discard.Remove(sleep.Id);
            player.Hand.Add(sleep.Id); sleep.Zone = CardZone.Hand;
            using var scope = BuildScope(specs, initial);
            var app = scope.Resolve<IGameApplication>();
            var data = scope.Resolve<GameData>();
            Assert.That(app.PlayCards(player.Id, new[] { sleep.Id }).Accepted, Is.True);
            Assert.That(app.DeclineSwitch(player.Id).Accepted, Is.True);
            Assert.That(app.Pass(player.Id).Accepted, Is.True);
            Assert.That(app.DeclineSwitch(player.Id).Accepted, Is.True);
            return data.Snapshot.Players[player.Id].Hp;
        }
        private static IScope BuildScope(GameSpecCatalog specs, GameState state)
        {
            var builder = new ScopeBuilder();
            builder.Bind<IGameSpecs>().FromInstance(specs);
            builder.Bind<GameData>().FromFactory(() => new GameData(state)).AsScoped();
            builder.Bind<IGameRulesDomain>().To<GameRulesDomain>().AsScoped();
            builder.Bind<IGameApplication>().To<GameApplication>().AsScoped();
            return builder.Build(name: "AuthoredTableIntegration");
        }
        private readonly struct EvidenceRow : ISpecRow
        {
            public EvidenceRow(string key, bool executable, int page) { Key = key; Executable = executable; Page = page; }
            public string Key { get; }
            public bool Executable { get; }
            public int Page { get; }
        }
    }
}
