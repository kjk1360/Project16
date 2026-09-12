using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Project16.Foundation.Specs
{
    /// <summary>
    /// Copies specification values at composition time. Rows must honor ISpecRow's
    /// immutable-value contract; no provider or mutable collection is retained.
    /// </summary>
    public sealed class SpecTable<T> : ISpecTable<T> where T : struct, ISpecRow
    {
        private readonly Dictionary<string, T> _byKey;
        private readonly ReadOnlyCollection<T> _rows;

        public SpecTable(IEnumerable<T> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            _byKey = new Dictionary<string, T>(StringComparer.Ordinal);
            var copy = new List<T>();
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Key))
                    throw new SpecValidationException($"{typeof(T).Name} contains an empty specification key.");
                if (_byKey.ContainsKey(row.Key))
                    throw new SpecValidationException($"{typeof(T).Name} contains duplicate specification key '{row.Key}'.");
                _byKey.Add(row.Key, row);
                copy.Add(row);
            }
            _rows = copy.AsReadOnly();
        }

        public int Count => _rows.Count;
        public IReadOnlyList<T> Rows => _rows;

        public T Get(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (_byKey.TryGetValue(key, out var row)) return row;
            throw new KeyNotFoundException($"{typeof(T).Name} has no specification with key '{key}'.");
        }

        public bool TryGet(string key, out T row)
        {
            if (key == null)
            {
                row = default;
                return false;
            }
            return _byKey.TryGetValue(key, out row);
        }
    }
}
