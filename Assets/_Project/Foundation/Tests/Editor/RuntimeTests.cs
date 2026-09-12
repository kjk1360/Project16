using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Kylin.DI;
using Kylin.DI.Layered;
using NUnit.Framework;
using Project16.Foundation.Composition;
using Project16.Foundation.Preferences;
using UnityEngine;

namespace Project16.Foundation.Tests
{
    public sealed class RuntimeTests
    {
        private string _storageRoot;
        private readonly List<GameRuntime> _runtimes = new();
        private readonly List<FoundationModule> _modules = new();

        [SetUp]
        public void SetUp()
        {
            _storageRoot = Path.Combine(Path.GetTempPath(), "Project16.Foundation.RuntimeTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_storageRoot);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var runtime in _runtimes) runtime.Dispose();
            _runtimes.Clear();
            foreach (var module in _modules) UnityEngine.Object.DestroyImmediate(module);
            _modules.Clear();
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, true);
        }

        [Test]
        public void SceneRequiresSession_AndDisposedRuntimeRejectsRestart()
        {
            var runtime = CreateRuntime();
            Assert.Throws<InvalidOperationException>(() => runtime.OpenScene("NoSession"));
            runtime.Dispose();
            Assert.Throws<ObjectDisposedException>(() => runtime.BeginSession("default"));
        }

        [Test]
        public void ChildOverrideAndDisposal_PreserveParentState()
        {
            var runtime = CreateRuntime(withModule: true);
            runtime.BeginSession("default");
            var firstScene = runtime.OpenScene("First");
            var secondScene = runtime.OpenScene("Second");
            var application = firstScene.ResolveEntry<ApplicationProbe>();
            var session = firstScene.ResolveEntry<SessionProbe>();
            var sceneState = firstScene.ResolveEntry<SceneProbe>();
            Assert.That(secondScene.ResolveEntry<ApplicationProbe>(), Is.SameAs(application));
            Assert.That(secondScene.ResolveEntry<SessionProbe>(), Is.SameAs(session));
            Assert.That(secondScene.ResolveEntry<SceneProbe>(), Is.Not.SameAs(sceneState));

            var feature = firstScene.OpenChild("IndependentFeature",
                builder => builder.Bind<SceneProbe>().ToSelf().AsScoped());
            var featureState = feature.ResolveEntry<SceneProbe>();
            Assert.That(featureState, Is.Not.SameAs(sceneState));
            Assert.That(feature.ResolveEntry<SessionProbe>(), Is.SameAs(session));
            var token = feature.Token;
            feature.Dispose();
            feature.Dispose();

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(feature.IsDisposed, Is.True);
            Assert.That(featureState.DisposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => feature.ResolveEntry<SceneProbe>());
            Assert.That(firstScene.ResolveEntry<SceneProbe>(), Is.SameAs(sceneState));
            Assert.That(sceneState.DisposeCount, Is.Zero);
            Assert.That(session.DisposeCount, Is.Zero);
            Assert.That(application.DisposeCount, Is.Zero);
        }

        [Test]
        public void ParentDisposal_CancelsNestedScopesAndDisposesEachServiceOnce()
        {
            var runtime = CreateRuntime(withModule: true);
            runtime.BeginSession("default");
            var scene = runtime.OpenScene("Scene");
            var child = scene.OpenChild("Screen");
            var grandchild = child.OpenChild("Popup",
                builder => builder.Bind<FeatureProbe>().ToSelf().AsScoped());
            var application = scene.ResolveEntry<ApplicationProbe>();
            var session = scene.ResolveEntry<SessionProbe>();
            var sceneState = scene.ResolveEntry<SceneProbe>();
            var featureState = grandchild.ResolveEntry<FeatureProbe>();
            var sceneToken = scene.Token;
            var childToken = child.Token;
            var grandchildToken = grandchild.Token;

            runtime.Dispose();
            runtime.Dispose();
            child.Dispose();
            grandchild.Dispose();

            Assert.That(scene.IsDisposed && child.IsDisposed && grandchild.IsDisposed, Is.True);
            Assert.That(sceneToken.IsCancellationRequested, Is.True);
            Assert.That(childToken.IsCancellationRequested, Is.True);
            Assert.That(grandchildToken.IsCancellationRequested, Is.True);
            Assert.That(application.DisposeCount, Is.EqualTo(1));
            Assert.That(session.DisposeCount, Is.EqualTo(1));
            Assert.That(sceneState.DisposeCount, Is.EqualTo(1));
            Assert.That(featureState.DisposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => scene.OpenChild("Closed"));
        }

        [Test]
        public void ScopeCancellation_PrecedesOwnedServiceDisposal()
        {
            var runtime = CreateRuntime();
            runtime.BeginSession("default");
            var feature = runtime.OpenScene("Scene").OpenChild("CancellableFeature", builder =>
                builder.Bind<CancellationProbe>().ToSelf().AsScoped());
            var probe = feature.ResolveEntry<CancellationProbe>();
            probe.Token = feature.Token;

            feature.Dispose();

            Assert.That(probe.WasCanceledDuringDispose, Is.True);
        }

        [Test]
        public void SessionReplacement_CreatesFreshSessionAndPreservesApplication()
        {
            var runtime = CreateRuntime(withModule: true);
            runtime.BeginSession("first");
            var first = runtime.OpenScene("First");
            var application = first.ResolveEntry<ApplicationProbe>();
            var firstSession = first.ResolveEntry<SessionProbe>();
            Assert.That(runtime.TryEndSession(), Is.True);
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(firstSession.DisposeCount, Is.EqualTo(1));
            Assert.That(application.DisposeCount, Is.Zero);
            Assert.That(runtime.HasSession, Is.False);
            Assert.That(runtime.CurrentProfile, Is.Null);

            runtime.BeginSession("second");
            var second = runtime.OpenScene("Second");
            Assert.That(second.ResolveEntry<ApplicationProbe>(), Is.SameAs(application));
            Assert.That(second.ResolveEntry<SessionProbe>(), Is.Not.SameAs(firstSession));
            Assert.That(runtime.CurrentProfile, Is.EqualTo("second"));
        }

        [Test]
        public void FailedChildConstruction_LeavesSceneUsable()
        {
            var runtime = CreateRuntime(withModule: true);
            runtime.BeginSession("default");
            var scene = runtime.OpenScene("Scene");
            var state = scene.ResolveEntry<SceneProbe>();

            Assert.Throws<InvalidOperationException>(() => scene.OpenChild("Broken", builder =>
            {
                builder.Bind<FeatureProbe>().ToSelf().AsScoped();
                builder.Bind<FeatureProbe>().ToSelf().AsScoped();
            }));

            Assert.That(scene.IsDisposed, Is.False);
            Assert.That(scene.ResolveEntry<SceneProbe>(), Is.SameAs(state));
            using var replacement = scene.OpenChild("Replacement");
            Assert.That(replacement.ResolveEntry<SceneProbe>(), Is.SameAs(state));
        }

        [Test]
        public void PreferencesOperations_SaveReloadAndDoNotDirtyEqualValues()
        {
            var runtime = CreateRuntime();
            runtime.BeginSession("default");
            var scene = runtime.OpenScene("Preferences");
            var data = scene.ResolveEntry<IUserPreferencesData>();
            var application = scene.ResolveEntry<IUserPreferencesApplication>();
            Assert.That(data.MusicVolume.Value, Is.InRange(0f, 1f));
            Assert.That(data.EffectsVolume.Value, Is.InRange(0f, 1f));
            Assert.That(data.Language.Value, Is.Not.Null);
            application.SetMusicVolume(0.25f);
            application.SetEffectsVolume(0.75f);
            application.SetLanguage("ko-KR");
            Assert.That(runtime.IsDirty, Is.True);
            var saved = runtime.FlushDirty();
            Assert.That(saved.Succeeded, Is.True);
            Assert.That(saved.SavedCount, Is.EqualTo(1));
            Assert.That(runtime.IsDirty, Is.False);

            application.SetMusicVolume(0.25f);
            application.SetEffectsVolume(0.75f);
            application.SetLanguage("ko-KR");
            Assert.That(runtime.IsDirty, Is.False);
            Assert.That(runtime.FlushDirty().AttemptedCount, Is.Zero);
            runtime.Dispose();

            var restarted = CreateRuntime();
            restarted.BeginSession("default");
            var restored = restarted.OpenScene("Reloaded").ResolveEntry<IUserPreferencesData>();
            Assert.That(restored.MusicVolume.Value, Is.EqualTo(0.25f));
            Assert.That(restored.EffectsVolume.Value, Is.EqualTo(0.75f));
            Assert.That(restored.Language.Value, Is.EqualTo("ko-KR"));
            Assert.That(restarted.IsDirty, Is.False);
            Assert.That(restarted.FlushDirty().AttemptedCount, Is.Zero);
        }

        [Test]
        public void Dispose_FlushesDirtySessionWithoutAnExplicitSave()
        {
            var runtime = CreateRuntime();
            runtime.BeginSession("default");
            runtime.OpenScene("BeforeQuit").ResolveEntry<IUserPreferencesApplication>()
                .SetLanguage("ja-JP");
            runtime.Dispose();

            var restarted = CreateRuntime();
            restarted.BeginSession("default");
            var data = restarted.OpenScene("AfterRestart").ResolveEntry<IUserPreferencesData>();
            Assert.That(data.Language.Value, Is.EqualTo("ja-JP"));
        }

        [Test]
        public void Dispose_CommitsChildCleanupBeforeTheFinalSessionSave()
        {
            var runtime = CreateRuntime();
            runtime.BeginSession("default");
            var feature = runtime.OpenScene("Scene").OpenChild("EditingPreferences", builder =>
                builder.Bind<CommitPreferencesOnClose>().ToSelf().AsScoped());
            feature.ResolveEntry<CommitPreferencesOnClose>();
            runtime.Dispose();

            var restarted = CreateRuntime();
            restarted.BeginSession("default");
            var data = restarted.OpenScene("Reloaded").ResolveEntry<IUserPreferencesData>();
            Assert.That(data.Language.Value, Is.EqualTo("close-commit"));
        }

        [Test]
        public void Profiles_KeepIndependentSavedState()
        {
            var runtime = CreateRuntime();
            runtime.BeginSession("first");
            runtime.OpenScene("First").ResolveEntry<IUserPreferencesApplication>().SetLanguage("ko-KR");
            Assert.That(runtime.TryEndSession(), Is.True);
            runtime.BeginSession("second");
            runtime.OpenScene("Second").ResolveEntry<IUserPreferencesApplication>().SetLanguage("ja-JP");
            Assert.That(runtime.TryEndSession(), Is.True);

            runtime.BeginSession("first");
            Assert.That(runtime.OpenScene("FirstAgain").ResolveEntry<IUserPreferencesData>().Language.Value,
                Is.EqualTo("ko-KR"));
            Assert.That(runtime.TryEndSession(), Is.True);
            runtime.BeginSession("second");
            Assert.That(runtime.OpenScene("SecondAgain").ResolveEntry<IUserPreferencesData>().Language.Value,
                Is.EqualTo("ja-JP"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("../other")]
        [TestCase("..\\other")]
        [TestCase("C:\\other")]
        [TestCase("/other")]
        [TestCase("a/b")]
        [TestCase("a.b")]
        [TestCase("CON")]
        [TestCase("aux")]
        [TestCase("COM1")]
        public void UnsafeProfile_IsRejectedBeforeOpeningSession(string profile)
        {
            var runtime = CreateRuntime();
            Assert.Throws<ArgumentException>(() => runtime.BeginSession(profile));
            Assert.That(runtime.HasSession, Is.False);
            Assert.That(Directory.GetFileSystemEntries(_storageRoot), Is.Empty);
        }

        [Test]
        public void FailedSessionSave_PreservesDirtyStateAndAllowsRetry()
        {
            var runtime = CreateRuntime();
            runtime.BeginSession("blocked");
            var scene = runtime.OpenScene("BeforeSave");
            scene.ResolveEntry<IUserPreferencesApplication>().SetLanguage("ko-KR");
            var blockedProfilePath = Path.Combine(_storageRoot, "blocked");
            File.WriteAllText(blockedProfilePath, "A file temporarily occupies the directory path.");

            Assert.That(runtime.TryEndSession(), Is.False);
            Assert.That(runtime.HasSession, Is.True);
            Assert.That(runtime.CurrentProfile, Is.EqualTo("blocked"));
            Assert.That(runtime.IsDirty, Is.True);
            Assert.That(runtime.LastSaveReport.Succeeded, Is.False);
            Assert.That(runtime.LastSaveReport.Failures.Count, Is.EqualTo(1));
            Assert.That(scene.IsDisposed, Is.True);

            File.Delete(blockedProfilePath);
            Assert.That(runtime.TryEndSession(), Is.True);
            Assert.That(runtime.HasSession, Is.False);
            runtime.BeginSession("blocked");
            Assert.That(runtime.OpenScene("AfterRetry").ResolveEntry<IUserPreferencesData>().Language.Value,
                Is.EqualTo("ko-KR"));
        }

        [Test]
        public void FoundationAssembly_HasNoInvalidLayerInjection()
        {
            Assert.DoesNotThrow(() => LayerValidator.ValidateAssembly(typeof(GameRuntime).Assembly));
        }

        [Test]
        public void SaveSchedule_AccumulatesUnscaledTimeAndCoalescesMissedIntervals()
        {
            var schedule = new SaveSchedule(30);
            Assert.That(schedule.Advance(15), Is.False);
            Assert.That(schedule.Advance(15), Is.True);
            Assert.That(schedule.Advance(75), Is.True);
            Assert.That(schedule.Advance(14), Is.False);
            Assert.That(schedule.Advance(1), Is.True);
            Assert.That(schedule.Advance(0), Is.False);
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void SaveSchedule_RejectsInvalidIntervals(double interval)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveSchedule(interval));
        }

        private GameRuntime CreateRuntime(bool withModule = false)
        {
            FoundationModule[] modules = null;
            if (withModule)
            {
                var module = ScriptableObject.CreateInstance<RuntimeTestModule>();
                _modules.Add(module);
                modules = new FoundationModule[] { module };
            }
            var runtime = new GameRuntime(_storageRoot, modules);
            _runtimes.Add(runtime);
            return runtime;
        }
    }

    public sealed class RuntimeTestModule : FoundationModule
    {
        public override void ConfigureApplication(ScopeBuilder builder) =>
            builder.Bind<ApplicationProbe>().ToSelf().AsSingleton();

        public override void ConfigureSession(ScopeBuilder builder, Persistence.LocalSaveSession saves) =>
            builder.Bind<SessionProbe>().ToSelf().AsScoped();

        public override void ConfigureScene(ScopeBuilder builder, string sceneName) =>
            builder.Bind<SceneProbe>().ToSelf().AsScoped();
    }

    public class DisposalProbe : IDataLayer, IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    public sealed class ApplicationProbe : DisposalProbe { }
    public sealed class SessionProbe : DisposalProbe { }
    public sealed class SceneProbe : DisposalProbe { }
    public sealed class FeatureProbe : DisposalProbe { }

    public sealed class CancellationProbe : IDataLayer, IDisposable
    {
        public CancellationToken Token { get; set; }
        public bool WasCanceledDuringDispose { get; private set; }
        public void Dispose() => WasCanceledDuringDispose = Token.IsCancellationRequested;
    }

    public sealed class CommitPreferencesOnClose : IViewModelLayer, IDisposable
    {
        [Inject] private IUserPreferencesApplication _application = null;
        public void Dispose() => _application.SetLanguage("close-commit");
    }
}
