using System;

namespace MackySoft.Navigathena
{

    /// <summary>Identifies one immutable route entry in a region history.</summary>
    public readonly struct NavigationEntryId : IEquatable<NavigationEntryId>
    {
        public NavigationEntryId (Guid value) => Value = value == Guid.Empty ? throw new ArgumentException("An identifier is required.", nameof(value)) : value;
        public Guid Value
        {
            get;
        }
        public bool Equals (NavigationEntryId other) => Value.Equals(other.Value);
        public override bool Equals (object? obj) => obj is NavigationEntryId other && Equals(other);
        public override int GetHashCode () => Value.GetHashCode();
        public override string ToString () => Value.ToString("D");
        public static bool operator == (NavigationEntryId left, NavigationEntryId right) => left.Equals(right);
        public static bool operator != (NavigationEntryId left, NavigationEntryId right) => !left.Equals(right);
    }

}
