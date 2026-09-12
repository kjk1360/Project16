using System.Collections.Generic;
using UnityEngine;

namespace Project16.Foundation.Composition
{
    [CreateAssetMenu(menuName = "Project16/Foundation Settings")]
    public sealed class FoundationSettings : ScriptableObject
    {
        public const string ResourcePath = "Project16/FoundationSettings";
        [SerializeField, Min(1)] private float _saveIntervalSeconds = 30;
        [SerializeField] private string _localProfile = "default";
        [SerializeField] private List<FoundationModule> _modules = new();

        public float SaveIntervalSeconds => _saveIntervalSeconds;
        public string LocalProfile => _localProfile;
        public IReadOnlyList<FoundationModule> Modules => _modules.AsReadOnly();
    }
}
