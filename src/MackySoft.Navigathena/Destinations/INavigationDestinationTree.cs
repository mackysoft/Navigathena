using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Represents an immutable route destination and its initial child-region destinations.</summary>
    public interface INavigationDestinationTree
    {
        NavigationRoute Route
        {
            get;
        }
        IReadOnlyDictionary<RegionDefinitionId, INavigationDestinationTree> Children
        {
            get;
        }
    }

}
