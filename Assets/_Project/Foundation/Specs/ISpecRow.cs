namespace Project16.Foundation.Specs
{
    /// <summary>
    /// A specification value identified by a stable, case-sensitive key.
    /// Implement as a readonly struct containing immutable values only. Do not store
    /// mutable collections, provider rows, Unity objects, or other Data services.
    /// </summary>
    public interface ISpecRow
    {
        string Key { get; }
    }
}
