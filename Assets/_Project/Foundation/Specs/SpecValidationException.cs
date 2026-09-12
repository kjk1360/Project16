using System;

namespace Project16.Foundation.Specs
{
    public sealed class SpecValidationException : Exception
    {
        public SpecValidationException(string message) : base(message) { }
        public SpecValidationException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
