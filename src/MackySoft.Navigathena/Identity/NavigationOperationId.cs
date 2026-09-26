using System;

namespace MackySoft.Navigathena
{

    /// <summary>Identifies one accepted navigation operation.</summary>
    public readonly struct NavigationOperationId : IEquatable<NavigationOperationId>
    {
        public NavigationOperationId (Guid value) => Value = value == Guid.Empty ? throw new ArgumentException("An identifier is required.", nameof(value)) : value;
        public Guid Value
        {
            get;
        }
        public bool Equals (NavigationOperationId other) => Value.Equals(other.Value);
        public override bool Equals (object? obj) => obj is NavigationOperationId other && Equals(other);
        public override int GetHashCode () => Value.GetHashCode();
        public override string ToString () => Value.ToString("D");
    }

}
