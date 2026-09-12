using System;
using System.IO;
using UnityEngine;

namespace Project16.Foundation.Composition
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
    public sealed class GameStarter : MonoBehaviour
    {
        private static GameStarter _active;
        private static int _playEpoch;
        [SerializeField] private FoundationSettings _settings;
        private GameRuntime _runtime;
        private SaveSchedule _schedule;
        private int _initializedEpoch;

        public GameRuntime Runtime => _runtime ?? throw new InvalidOperationException("Game runtime is not initialized.");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = null;
            _playEpoch++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RestoreApplication()
        {
            var starter = FindFirstObjectByType<GameStarter>();
            if (starter != null && starter.isActiveAndEnabled) EnsureStarted(starter._settings);
        }

        public static GameStarter EnsureStarted(FoundationSettings settings = null)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("GameStarter requires Play Mode.");
            if (_active != null)
            {
                if (settings != null && settings != _active._settings)
                    throw new InvalidOperationException("All scene starters must share the same FoundationSettings asset.");
                return _active;
            }

            var existing = FindFirstObjectByType<GameStarter>();
            if (existing != null)
            {
                existing.Initialize(settings != null ? settings : existing._settings);
                return existing;
            }

            var root = new GameObject("[Project16] GameStarter");
            root.SetActive(false);
            try
            {
                var starter = root.AddComponent<GameStarter>();
                starter.Initialize(settings);
                root.SetActive(true);
                return starter;
            }
            catch
            {
                Destroy(root);
                throw;
            }
        }

        private void Awake()
        {
            if (_active != null && _active != this)
            {
                Destroy(gameObject);
                return;
            }
            Initialize(_settings);
        }

        private void OnEnable()
        {
            if (Application.isPlaying) Awake();
        }

        private void Initialize(FoundationSettings settings)
        {
            if (_runtime != null && _initializedEpoch == _playEpoch) return;
            _settings = settings != null ? settings : Resources.Load<FoundationSettings>(FoundationSettings.ResourcePath);
            if (_settings == null) throw new InvalidOperationException("Create the foundation assets with Tools/Project16/Create Foundation Assets.");
            if (_runtime != null) _runtime.Dispose();
            var runtime = new GameRuntime(Path.Combine(Application.persistentDataPath, "UserData"), _settings.Modules);
            try
            {
                _schedule = new SaveSchedule(_settings.SaveIntervalSeconds);
                runtime.BeginSession(_settings.LocalProfile);
            }
            catch { runtime.Dispose(); throw; }
            _runtime = runtime;
            _initializedEpoch = _playEpoch;
            _active = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (_runtime != null && _schedule.Advance(Time.unscaledDeltaTime)) Save("interval");
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Save("background");
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Save("focus lost");
        }

        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() => Shutdown();

        private void Save(string reason)
        {
            if (_runtime == null) return;
            try { ReportSaveFailure(_runtime.FlushDirty(), reason); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private void Shutdown()
        {
            var runtime = _runtime;
            _runtime = null;
            if (_active == this) _active = null;
            if (runtime == null) return;
            try
            {
                runtime.Dispose();
                ReportSaveFailure(runtime.LastSaveReport, "shutdown");
            }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }

        private static void ReportSaveFailure(Persistence.SaveReport report, string reason)
        {
            if (report == null || report.Succeeded) return;
            if (report.IsBusy) Debug.LogError($"Local save was already running ({reason}).");
            foreach (var failure in report.Failures)
                Debug.LogError($"Local save failed ({reason}, unit '{failure.Key}'): {failure.Message}");
        }
    }
}
