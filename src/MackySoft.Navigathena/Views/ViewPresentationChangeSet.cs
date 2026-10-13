using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{
    /// <summary> Describes one related set of screen presentation permissions. </summary>
    public sealed class ViewPresentationChangeSet
    {
        public ViewPresentationChangeSet (IReadOnlyList<ViewPresentationChange> changes)
        {
            if (changes is null)
            {
                throw new ArgumentNullException(nameof(changes));
            }
            if (changes.Any(change => change.View is null)
                || changes.Select(change => change.View).Distinct().Count() != changes.Count)
            {
                throw new NavigationConfigurationException("A presentation change set requires distinct, non-null views.");
            }
            Changes = Array.AsReadOnly(changes.ToArray());
        }

        public IReadOnlyList<ViewPresentationChange> Changes { get; }
    }
}
