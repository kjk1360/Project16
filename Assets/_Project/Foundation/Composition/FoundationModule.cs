using Kylin.DI;
using Project16.Foundation.Persistence;
using UnityEngine;

namespace Project16.Foundation.Composition
{
    /// <summary>Authored composition only. Modules never retain live services or scopes.</summary>
    public abstract class FoundationModule : ScriptableObject
    {
        public virtual void ConfigureApplication(ScopeBuilder builder) { }
        public virtual void ConfigureSession(ScopeBuilder builder, LocalSaveSession saves) { }
        public virtual void OnSessionBuilt(IScope scope, LocalSaveSession saves) { }
        public virtual void ConfigureScene(ScopeBuilder builder, string sceneName) { }
    }
}
