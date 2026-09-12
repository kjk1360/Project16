using System;
using Kylin.DI;
using UnityEngine;

namespace Project16.Foundation.Composition
{
    /// <summary>One composition root per authored scene. Its hierarchy is externally owned by Unity.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-9000)]
    public class SceneStarter : MonoBehaviour
    {
        [SerializeField] private FoundationSettings _settings;
        [SerializeField] private GameObject _injectionRoot;
        private ScopeHandle _scope;

        public ScopeHandle SceneScope => _scope ?? throw new InvalidOperationException("Scene scope has not started.");

        protected virtual void Configure(ScopeBuilder builder) { }

        private void Awake() => Initialize();

        private void OnEnable()
        {
            if (Application.isPlaying) Initialize();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RestoreSceneScopes()
        {
            // With Scene Reload disabled, Unity keeps these objects but KDI resets its scopes.
            foreach (var starter in FindObjectsByType<SceneStarter>(FindObjectsSortMode.None))
                if (starter.isActiveAndEnabled) starter.Initialize();
        }

        public void Initialize()
        {
            if (_scope != null && !_scope.IsDisposed) return;
            var root = _injectionRoot != null ? _injectionRoot : gameObject;
            var scope = GameStarter.EnsureStarted(_settings).Runtime.OpenScene(gameObject.scene.name, Configure);
            try
            {
                scope.InjectHierarchy(root);
                _scope = scope;
            }
            catch { scope.Dispose(); throw; }
        }

        private void OnDestroy()
        {
            _scope?.Dispose();
            _scope = null;
        }
    }
}
