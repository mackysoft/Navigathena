using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena.Presentation
{
    /// <summary>Describes the before and after contents of one directly owned child-region instance.</summary>
    public sealed class ChildRegionChange
    {
        internal ChildRegionChange (RegionDefinitionId definitionId, RegionInstanceId instanceId, IReadOnlyList<ChildRegionEntry> beforeEntries, IReadOnlyList<ChildRegionEntry> afterEntries)
        {
            DefinitionId = definitionId;
            InstanceId = instanceId;
            BeforeEntries = Array.AsReadOnly(beforeEntries.ToArray());
            AfterEntries = Array.AsReadOnly(afterEntries.ToArray());
        }

        public RegionDefinitionId DefinitionId
        {
            get;
        }
        public RegionInstanceId InstanceId
        {
            get;
        }
        public IReadOnlyList<ChildRegionEntry> BeforeEntries
        {
            get;
        }
        public IReadOnlyList<ChildRegionEntry> AfterEntries
        {
            get;
        }
    }
}
