using System;

namespace MackySoft.Navigathena
{

    /// <summary>Identifies a region slot in an immutable navigation definition.</summary>
    public readonly struct RegionDefinitionId : IEquatable<RegionDefinitionId>
    {
        public RegionDefinitionId (string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A region definition identifier is required.", nameof(value));
            }

            Value = value;
        }

        public string Value
        {
            get;
        }

        public bool Equals (RegionDefinitionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals (object? obj) => obj is RegionDefinitionId other && Equals(other);
        public override int GetHashCode () => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString () => Value ?? string.Empty;
        public static bool operator == (RegionDefinitionId left, RegionDefinitionId right) => left.Equals(right);
        public static bool operator != (RegionDefinitionId left, RegionDefinitionId right) => !left.Equals(right);
    }

}
