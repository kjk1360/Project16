using System.Collections.Generic;
using Kylin.DI.Layered;

namespace Project16.Foundation.Specs
{
    /// <summary>A detached, read-only specification snapshot owned by its binding Scope.</summary>
    public interface ISpecTable<T> : IDataLayer where T : struct, ISpecRow
    {
        int Count { get; }
        IReadOnlyList<T> Rows { get; }
        T Get(string key);
        bool TryGet(string key, out T row);
    }
}
