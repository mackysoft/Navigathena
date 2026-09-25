using System;

namespace MackySoft.Navigathena
{

    /// <summary>Identifies a presentation-loss or host incident that can be recovered by the navigation host.</summary>
    public readonly struct NavigationIncidentId : IEquatable<NavigationIncidentId>
    {
        public NavigationIncidentId (Guid value) => Value = value == Guid.Empty ? throw new ArgumentException("An identifier is required.", nameof(value)) : value;
        public Guid Value
        {
            get;
        }
        public bool Equals (NavigationIncidentId other) => Value.Equals(other.Value);
        public override bool Equals (object? obj) => obj is NavigationIncidentId other && Equals(other);
        public override int GetHashCode () => Value.GetHashCode();
        public override string ToString () => Value.ToString("D");
    }

}
