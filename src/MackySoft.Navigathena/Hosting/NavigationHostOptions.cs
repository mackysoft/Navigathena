using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Supplies observers for a host without exposing its runtime or mailbox.</summary>
    public sealed class NavigationHostOptions
    {
        private static readonly IReadOnlyList<INavigationCommitObserver> EmptyCommitObservers = Array.AsReadOnly(Array.Empty<INavigationCommitObserver>());

        /// <summary>Initializes host observer configuration.</summary>
        /// <param name="commitObservers">Observers to notify, or null to configure none. Their order is copied when this configuration is created.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="commitObservers" /> contains a null observer.</exception>
        public NavigationHostOptions (IReadOnlyList<INavigationCommitObserver>? commitObservers = null)
        {
            if (commitObservers is null)
            {
                CommitObservers = EmptyCommitObservers;
                return;
            }

            INavigationCommitObserver[] observers = new INavigationCommitObserver[commitObservers.Count];
            for (int i = 0; i < observers.Length; i++)
            {
                observers[i] = commitObservers[i] ?? throw new ArgumentNullException(nameof(commitObservers));
            }

            CommitObservers = Array.AsReadOnly(observers);
        }

        /// <summary>Gets the observers in the order owned by this configuration.</summary>
        public IReadOnlyList<INavigationCommitObserver> CommitObservers
        {
            get;
        }

        public IReadOnlyDictionary<RegionDefinitionId, RegionNavigationOptions> Regions { get; init; } = new Dictionary<RegionDefinitionId, RegionNavigationOptions>();
        public Action<NavigationResult>? OperationCompleted
        {
            get; init;
        }
        /// <summary>Gets the shared blocker definition, instantiated at most once at a time within this host.</summary>
        public BlockerDefinition? DefaultBlocker
        {
            get; init;
        }
    }

}
