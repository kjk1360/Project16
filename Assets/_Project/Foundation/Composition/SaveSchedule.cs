using System;

namespace Project16.Foundation.Composition
{
    /// <summary>Receives unscaled elapsed time; coalesces missed intervals into one save.</summary>
    public sealed class SaveSchedule
    {
        private readonly double _interval;
        private double _elapsed;

        public SaveSchedule(double intervalSeconds)
        {
            if (double.IsNaN(intervalSeconds) || double.IsInfinity(intervalSeconds) || intervalSeconds < 1)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            _interval = intervalSeconds;
        }

        public bool Advance(double unscaledSeconds)
        {
            if (double.IsNaN(unscaledSeconds) || double.IsInfinity(unscaledSeconds) || unscaledSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(unscaledSeconds));
            _elapsed += unscaledSeconds;
            if (_elapsed < _interval) return false;
            _elapsed %= _interval;
            return true;
        }
    }
}
