using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Lists immutable entries created and removed by one logical commit.</summary>
    public sealed record NavigationDelta (IReadOnlyList<NavigationEntry> CreatedEntries, IReadOnlyList<NavigationEntry> RemovedEntries)
    {
        public static NavigationDelta Empty { get; } = new(Array.Empty<NavigationEntry>(), Array.Empty<NavigationEntry>());
    }

}
