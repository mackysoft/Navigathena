using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Provides the complete planned logical transition before physical preparation begins.</summary>
    public sealed class PresentationTransition
    {
        internal bool WaitForTermination
        {
            get; init;
        }
        internal CallChange? CallChange
        {
            get; init;
        }
        internal PresentationTransition (NavigationOperationId operationId, NavigationOperationKind operation, NavigationState before, NavigationState proposedAfter, EffectiveComposition beforeComposition, EffectiveComposition proposedComposition, IReadOnlyList<NavigationEntryId> allowedDepartureEntries, PresentationRecoveryContext? recovery = null, RegionInstanceId? targetRegion = null, NavigationOptions? options = null)
        {
            OperationId = operationId;
            Operation = operation;
            Before = before;
            ProposedAfter = proposedAfter;
            BeforeComposition = beforeComposition;
            ProposedComposition = proposedComposition;
            AllowedDepartureEntries = Array.AsReadOnly(allowedDepartureEntries.ToArray());
            Recovery = recovery;
            TargetRegion = targetRegion;
            Options = options;
        }

        public NavigationOperationId OperationId
        {
            get;
        }
        public NavigationOperationKind Operation
        {
            get;
        }
        public RegionInstanceId? TargetRegion
        {
            get;
        }
        public NavigationOptions? Options
        {
            get;
        }
        public NavigationState Before
        {
            get;
        }
        public NavigationState ProposedAfter
        {
            get;
        }
        public EffectiveComposition BeforeComposition
        {
            get;
        }
        public EffectiveComposition ProposedComposition
        {
            get;
        }
        public IReadOnlyList<NavigationEntryId> AllowedDepartureEntries
        {
            get;
        }

        public PresentationRecoveryContext? Recovery
        {
            get;
        }
        internal NavigationOperation? ActiveOperation
        {
            get; set;
        }
    }

}
