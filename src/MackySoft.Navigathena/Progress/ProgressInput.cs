using System;

namespace MackySoft.Navigathena
{
    /// <summary>Identifies a typed reporting contract shared by a producer and its progress definition.</summary>
    /// <remarks>Identity is the input instance, not its diagnostic name. Values and their referenced data must not be changed after reporting.</remarks>
    public sealed class ProgressInput<T>
    {
        /// <summary>Creates a distinct input with a nonempty name used only in diagnostics.</summary>
        public ProgressInput (string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A diagnostic input name is required.", nameof(name));
            }
            Name = name;
        }

        /// <summary>The diagnostic name, not a global routing key.</summary>
        public string Name { get; }
    }
}
