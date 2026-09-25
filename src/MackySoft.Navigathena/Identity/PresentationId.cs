using System;

namespace MackySoft.Navigathena
{

    /// <summary>Identifies one physical realization of a navigation entry.</summary>
    public readonly struct PresentationId : IEquatable<PresentationId>
    {
        public PresentationId (Guid value) => Value = value == Guid.Empty ? throw new ArgumentException("An identifier is required.", nameof(value)) : value;
        public Guid Value
        {
            get;
        }
        public bool Equals (PresentationId other) => Value.Equals(other.Value);
        public override bool Equals (object? obj) => obj is PresentationId other && Equals(other);
        public override int GetHashCode () => Value.GetHashCode();
        public override string ToString () => Value.ToString("D");
        public static bool operator == (PresentationId left, PresentationId right) => left.Equals(right);
        public static bool operator != (PresentationId left, PresentationId right) => !left.Equals(right);
    }

}
