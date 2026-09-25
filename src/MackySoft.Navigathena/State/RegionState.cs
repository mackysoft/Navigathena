using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{

    /// <summary>Stores the ordered history of one root or entry-owned region instance.</summary>
    public sealed class RegionState
    {
        internal RegionState (RegionInstanceId id, RegionDefinitionId definitionId, NavigationEntryId? ownerEntryId, IReadOnlyList<NavigationEntryId> entries)
        {
            Id = id;
            DefinitionId = definitionId;
            OwnerEntryId = ownerEntryId;
            Entries = Array.AsReadOnly(entries.ToArray());
        }

        public RegionInstanceId Id
        {
            get;
        }
        public RegionDefinitionId DefinitionId
        {
            get;
        }
        public NavigationEntryId? OwnerEntryId
        {
            get;
        }
        public IReadOnlyList<NavigationEntryId> Entries
        {
            get;
        }
    }

}
