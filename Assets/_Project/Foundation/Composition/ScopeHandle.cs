using System;
using System.Collections.Generic;
using System.Threading;
using Kylin.DI;
using UnityEngine;

namespace Project16.Foundation.Composition
{
    /// <summary>A composition boundary for a scene, screen or feature.</summary>
    public sealed class ScopeHandle : IDisposable
    {
        private readonly IScope _scope;
        private readonly ScopeCancellation _lifetime;
        private readonly List<ScopeHandle> _children = new();
        private bool _closed;

        internal ScopeHandle(IScope parent, string name, Action<ScopeBuilder> configure)
        {
            var builder = new ScopeBuilder();
            builder.Bind<ScopeCancellation>().ToSelf().AsEntryPoint().AsScoped();
            configure?.Invoke(builder);
            _scope = builder.Build(parent, name);
            try { _lifetime = _scope.Resolve<ScopeCancellation>(); }
            catch { _scope.Dispose(); throw; }
        }

        public bool IsDisposed => _closed || _lifetime.IsDisposed;
        public CancellationToken Token => _lifetime.Token;

        /// <summary>Resolve only at immediate composition startup, never in business code.</summary>
        public T ResolveEntry<T>() where T : class
        {
            ThrowIfDisposed();
            return _scope.Resolve<T>();
        }

        public void InjectHierarchy(GameObject root)
        {
            ThrowIfDisposed();
            _scope.InjectGameObject(root);
        }

        public ScopeHandle OpenChild(string name, Action<ScopeBuilder> configure = null)
        {
            ThrowIfDisposed();
            _children.RemoveAll(child => child.IsDisposed);
            var child = new ScopeHandle(_scope, name, configure);
            _children.Add(child);
            return child;
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            try { _lifetime.Dispose(); }
            catch (Exception exception) { Debug.LogException(exception); }
            for (var i = _children.Count - 1; i >= 0; i--) _children[i].Dispose();
            _children.Clear();
            _scope.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(ScopeHandle));
        }
    }

    public sealed class ScopeCancellation : IDependencyObject, IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        public CancellationToken Token { get; }
        public bool IsDisposed { get; private set; }

        public ScopeCancellation() => Token = _source.Token;

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            try { _source.Cancel(); }
            finally { _source.Dispose(); }
        }
    }
}
