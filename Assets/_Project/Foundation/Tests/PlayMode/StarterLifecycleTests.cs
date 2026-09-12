using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Project16.Foundation.Composition;
using Project16.Foundation.Preferences;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project16.Foundation.PlayModeTests
{
    /// <summary>
    /// Lifecycle handlers operate on a precomposed temporary runtime. No test uses the
    /// player's persistentDataPath, changes a saved settings asset, or quits the Editor.
    /// </summary>
    public sealed class StarterLifecycleTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly List<GameObject> _objects = new();
        private readonly List<GameRuntime> _runtimes = new();
        private FoundationSettings _settings;
        private GameStarter _host;
        private GameRuntime _runtime;
        private string _storageRoot;
        private float _previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            _previousTimeScale = Time.timeScale;
            Assert.That(UnityEngine.Object.FindObjectsByType<GameStarter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None), Is.Empty,
                "These tests require an isolated test scene; an existing game host must not be replaced.");
            Assert.That(GetStaticField("_active"), Is.Null,
                "A pre-existing application host must not be replaced by a test.");

            _storageRoot = Path.Combine(Path.GetTempPath(), "Project16.Foundation.PlayModeTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_storageRoot);
            _settings = ScriptableObject.CreateInstance<FoundationSettings>();
            _runtime = CreateRuntime();
            _runtime.BeginSession("test-profile");
            Assert.That(_runtime.FlushDirty().Succeeded, Is.True);

            var root = NewInactiveObject("Test GameStarter");
            _host = root.AddComponent<GameStarter>();
            SetField(_host, "_settings", _settings);
            SetField(_host, "_runtime", _runtime);
            SetField(_host, "_schedule", new SaveSchedule(3600));
            SetField(_host, "_initializedEpoch", GetStaticField("_playEpoch"));
            SetStaticField("_active", _host);
            root.SetActive(true); // Runs the actual Awake and OnEnable handlers.
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _previousTimeScale;
            for (var i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) UnityEngine.Object.Destroy(_objects[i]);
            yield return null; // Complete the real OnDestroy callbacks before deleting test files.
            foreach (var runtime in _runtimes) runtime.Dispose();
            _runtimes.Clear();
            _objects.Clear();
            if (ReferenceEquals(GetStaticField("_active"), _host)) SetStaticField("_active", null);
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            _settings = null;
            _host = null;
            _runtime = null;
            if (_storageRoot != null && Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, true);
            _storageRoot = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator PauseAndFocusLoss_FlushDirtyUnitsAndSkipCleanWrites()
        {
            var application = PreferencesApplication();
            application.SetLanguage("pause-checkpoint");
            _host.SendMessage("OnApplicationPause", false, SendMessageOptions.RequireReceiver);
            Assert.That(_runtime.IsDirty, Is.True);
            _host.SendMessage("OnApplicationPause", true, SendMessageOptions.RequireReceiver);
            Assert.That(_runtime.IsDirty, Is.False);
            Assert.That(_runtime.LastSaveReport.SavedCount, Is.EqualTo(1));
            var path = Path.Combine(_storageRoot, "test-profile", "preferences.mpk");
            var pauseBytes = File.ReadAllBytes(path);

            _host.SendMessage("OnApplicationPause", true, SendMessageOptions.RequireReceiver);
            Assert.That(_runtime.LastSaveReport.AttemptedCount, Is.Zero);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(pauseBytes));

            application.SetLanguage("focus-checkpoint");
            _host.SendMessage("OnApplicationFocus", true, SendMessageOptions.RequireReceiver);
            Assert.That(_runtime.IsDirty, Is.True);
            _host.SendMessage("OnApplicationFocus", false, SendMessageOptions.RequireReceiver);
            Assert.That(_runtime.IsDirty, Is.False);
            Assert.That(_runtime.LastSaveReport.SavedCount, Is.EqualTo(1));
            _host.SendMessage("OnApplicationQuit", SendMessageOptions.RequireReceiver);
            Assert.That(ReloadPreferences().Language.Value, Is.EqualTo("focus-checkpoint"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Update_AutosavesUsingUnscaledTimeWhileGameTimeIsPaused()
        {
            var schedule = new SaveSchedule(1);
            schedule.Advance(0.95);
            SetField(_host, "_schedule", schedule);
            PreferencesApplication().SetLanguage("interval-checkpoint");
            Time.timeScale = 0;
            var deadline = Time.realtimeSinceStartupAsDouble + 2;

            while (_runtime.IsDirty && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;

            Assert.That(_runtime.IsDirty, Is.False, "GameStarter.Update did not advance the unscaled save schedule.");
            Assert.That(_runtime.LastSaveReport.SavedCount, Is.EqualTo(1));
            Time.timeScale = _previousTimeScale;
            _host.SendMessage("OnApplicationQuit", SendMessageOptions.RequireReceiver);
            Assert.That(ReloadPreferences().Language.Value, Is.EqualTo("interval-checkpoint"));
        }

        [UnityTest]
        public IEnumerator Quit_FlushesAndClosesChildScopesBeforeHostDestruction()
        {
            var scene = _runtime.OpenScene("BeforeQuit");
            var feature = scene.OpenChild("Feature");
            var token = feature.Token;
            feature.ResolveEntry<IUserPreferencesApplication>().SetLanguage("quit-checkpoint");

            _host.SendMessage("OnApplicationQuit", SendMessageOptions.RequireReceiver);

            Assert.That(_runtime.HasSession, Is.False);
            Assert.That(scene.IsDisposed && feature.IsDisposed, Is.True);
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.Throws<InvalidOperationException>(() => _ = _host.Runtime);
            Assert.That(_runtime.LastSaveReport.SavedCount, Is.EqualTo(1));
            Assert.That(ReloadPreferences().Language.Value, Is.EqualTo("quit-checkpoint"));
            UnityEngine.Object.Destroy(_host.gameObject);
            yield return null; // OnDestroy must tolerate the completed quit shutdown.
            Assert.That(GetStaticField("_active"), Is.Null);
        }

        [UnityTest]
        public IEnumerator DuplicateHost_IsDestroyedWithoutDisposingTheOriginalRuntime()
        {
            var duplicateObject = NewInactiveObject("Duplicate GameStarter");
            duplicateObject.AddComponent<GameStarter>();
            duplicateObject.SetActive(true);
            yield return null;

            Assert.That(duplicateObject == null, Is.True);
            Assert.That(_host != null, Is.True);
            Assert.That(_host.Runtime, Is.SameAs(_runtime));
            Assert.That(GameStarter.EnsureStarted(_settings), Is.SameAs(_host));
            Assert.That(_runtime.HasSession, Is.True);
            PreferencesApplication().SetLanguage("original-survives");
            Assert.That(_runtime.FlushDirty().Succeeded, Is.True);
        }

        [UnityTest]
        public IEnumerator SceneStarter_ReusesLiveScopeAndRebuildsClosedScopeOnEnableAndReloadHook()
        {
            var sceneObject = NewInactiveObject("Test SceneStarter");
            var starter = sceneObject.AddComponent<SceneStarter>();
            SetField(starter, "_settings", _settings);
            sceneObject.SetActive(true);
            var original = starter.SceneScope;
            var sessionData = original.ResolveEntry<IUserPreferencesData>();
            sceneObject.SetActive(false);
            Assert.That(original.IsDisposed, Is.False, "Disabling a scene object is not scene lifetime completion.");
            sceneObject.SetActive(true);
            Assert.That(starter.SceneScope, Is.SameAs(original));

            sceneObject.SetActive(false);
            original.Dispose();
            sceneObject.SetActive(true);
            var rebuiltOnEnable = starter.SceneScope;
            Assert.That(rebuiltOnEnable, Is.Not.SameAs(original));
            Assert.That(rebuiltOnEnable.ResolveEntry<IUserPreferencesData>(), Is.SameAs(sessionData));

            rebuiltOnEnable.Dispose();
            InvokeStatic(typeof(GameStarter), "RestoreApplication");
            InvokeStatic(typeof(SceneStarter), "RestoreSceneScopes");
            var rebuiltByHook = starter.SceneScope;
            Assert.That(rebuiltByHook, Is.Not.SameAs(rebuiltOnEnable));
            Assert.That(rebuiltByHook.IsDisposed, Is.False);
            InvokeStatic(typeof(SceneStarter), "RestoreSceneScopes");
            Assert.That(starter.SceneScope, Is.SameAs(rebuiltByHook));

            UnityEngine.Object.Destroy(sceneObject);
            yield return null;
            Assert.That(rebuiltByHook.IsDisposed, Is.True);
            Assert.That(_runtime.HasSession, Is.True);
        }

        private GameRuntime CreateRuntime()
        {
            var runtime = new GameRuntime(_storageRoot);
            _runtimes.Add(runtime);
            return runtime;
        }

        private IUserPreferencesApplication PreferencesApplication() =>
            _runtime.OpenScene("TestCommands").ResolveEntry<IUserPreferencesApplication>();

        private IUserPreferencesData ReloadPreferences()
        {
            var restored = CreateRuntime();
            restored.BeginSession("test-profile");
            return restored.OpenScene("Restored").ResolveEntry<IUserPreferencesData>();
        }

        private GameObject NewInactiveObject(string name)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            _objects.Add(gameObject);
            return gameObject;
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

        private static object GetStaticField(string name) =>
            typeof(GameStarter).GetField(name, PrivateStatic).GetValue(null);

        private static void SetStaticField(string name, object value) =>
            typeof(GameStarter).GetField(name, PrivateStatic).SetValue(null, value);

        private static void InvokeStatic(Type type, string name) =>
            type.GetMethod(name, PrivateStatic).Invoke(null, null);
    }
}
