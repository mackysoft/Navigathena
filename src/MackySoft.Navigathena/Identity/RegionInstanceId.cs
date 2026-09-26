using System;

namespace MackySoft.Navigathena
{

    /// <summary>Identifies one region instance owned by an entry or by the host root.</summary>
    public readonly struct RegionInstanceId : IEquatable<RegionInstanceId>
    {
        public RegionInstanceId (Guid value) => Value = value == Guid.Empty ? throw new ArgumentException("An identifier is required.", nameof(value)) : value;
        public Guid Value
        {
            get;
        }
        public bool Equals (RegionInstanceId other) => Value.Equals(other.Value);
        public override bool Equals (object? obj) => obj is RegionInstanceId other && Equals(other);
        public override int GetHashCode () => Value.GetHashCode();
        public override string ToString () => Value.ToString("D");
        public static bool operator == (RegionInstanceId left, RegionInstanceId right) => left.Equals(right);
        public static bool operator != (RegionInstanceId left, RegionInstanceId right) => !left.Equals(right);
    }

}
