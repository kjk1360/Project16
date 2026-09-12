using System;
using System.Collections.Generic;
using System.IO;
using Kylin.DI;
using Project16.Foundation.Persistence;
using Project16.Foundation.Preferences;

namespace Project16.Foundation.Composition
{
    /// <summary>Owns the application and local session. This is not a resolvable service.</summary>
    public sealed class GameRuntime : IDisposable
    {
        private readonly IScope _application;
        private readonly string _storageRoot;
        private readonly FoundationModule[] _modules;
        private readonly List<ScopeHandle> _scenes = new();
        private IScope _session;
        private LocalSaveSession _saves;
        private bool _disposed;

        public GameRuntime(string storageRoot, IReadOnlyList<FoundationModule> modules = null)
        {
            _storageRoot = Path.GetFullPath(storageRoot ?? throw new ArgumentNullException(nameof(storageRoot)));
            _modules = new FoundationModule[modules?.Count ?? 0];
            var unique = new HashSet<FoundationModule>();
            var builder = new ScopeBuilder();
            for (var i = 0; i < _modules.Length; i++)
            {
                var module = modules[i];
                if (module == null || !unique.Add(module))
                    throw new ArgumentException("Foundation modules must be assigned and unique.", nameof(modules));
                _modules[i] = module;
                module.ConfigureApplication(builder);
            }
            _application = builder.Build(name: "ApplicationScope");
        }

        public bool HasSession => _session != null;
        public bool IsDirty => _saves?.IsDirty ?? false;
        public SaveReport LastSaveReport { get; private set; }
        public string CurrentProfile { get; private set; }

        public void BeginSession(string profile)
        {
            ThrowIfDisposed();
            if (HasSession) throw new InvalidOperationException("Close the current session before opening another profile.");
            ValidateProfile(profile);
            var saves = new LocalSaveSession(Path.Combine(_storageRoot, profile));
            IScope scope = null;
            try
            {
                var preferences = saves.Load(UserPreferencesData.SaveKey, 1, UserPreferencesSnapshot.Default);
                var builder = new ScopeBuilder();
                builder.Bind<UserPreferencesData>()
                    .FromFactory(() => new UserPreferencesData(preferences))
                    .AlsoBind<IUserPreferencesData>().AsScoped();
                builder.Bind<IUserPreferencesDomain>().To<UserPreferencesDomain>().AsScoped();
                builder.Bind<IUserPreferencesApplication>().To<UserPreferencesApplication>().AsScoped();
                foreach (var module in _modules) module.ConfigureSession(builder, saves);
                scope = builder.Build(_application, "LocalSessionScope:" + profile);
                saves.Track(scope.Resolve<UserPreferencesData>());
                foreach (var module in _modules) module.OnSessionBuilt(scope, saves);
                _session = scope;
                _saves = saves;
                CurrentProfile = profile;
            }
            catch
            {
                scope?.Dispose();
                // An incomplete session must never write defaults over a failed load.
                saves.Abandon();
                throw;
            }
        }

        public ScopeHandle OpenScene(string name, Action<ScopeBuilder> configure = null)
        {
            ThrowIfDisposed();
            if (!HasSession) throw new InvalidOperationException("Start a local session before a scene scope.");
            _scenes.RemoveAll(scene => scene.IsDisposed);
            var scene = new ScopeHandle(_session, "SceneScope:" + name, builder =>
            {
                foreach (var module in _modules) module.ConfigureScene(builder, name);
                configure?.Invoke(builder);
            });
            _scenes.Add(scene);
            return scene;
        }

        public SaveReport FlushDirty()
        {
            ThrowIfDisposed();
            if (_saves == null) return LastSaveReport;
            return LastSaveReport = _saves.FlushDirty();
        }

        /// <summary>Failure keeps the session available for retry; scene handles have been closed.</summary>
        public bool TryEndSession()
        {
            ThrowIfDisposed();
            if (!HasSession) return true;
            CloseScenes();
            LastSaveReport = _saves.FlushDirty();
            if (!LastSaveReport.Succeeded || _saves.IsDirty) return false;
            ReleaseSession();
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                CloseScenes();
                if (_saves != null) LastSaveReport = _saves.FlushDirty();
            }
            finally
            {
                _disposed = true;
                try { ReleaseSession(); }
                finally { _application.Dispose(); }
            }
        }

        private void CloseScenes()
        {
            for (var i = _scenes.Count - 1; i >= 0; i--) _scenes[i].Dispose();
            _scenes.Clear();
        }

        private void ReleaseSession()
        {
            var scope = _session;
            var saves = _saves;
            _session = null;
            _saves = null;
            CurrentProfile = null;
            try { scope?.Dispose(); }
            finally { saves?.Abandon(); }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GameRuntime));
        }

        private static void ValidateProfile(string profile)
        {
            if (string.IsNullOrWhiteSpace(profile) || profile.Length > 64)
                throw new ArgumentException("Profile must contain 1-64 ASCII letters, digits, '_' or '-'.", nameof(profile));
            foreach (var c in profile)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-'))
                    throw new ArgumentException("Profile contains an invalid path character.", nameof(profile));
            var upper = profile.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL" ||
                upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) &&
                upper[3] >= '1' && upper[3] <= '9')
                throw new ArgumentException("Profile is a reserved filesystem name.", nameof(profile));
        }
    }
}
