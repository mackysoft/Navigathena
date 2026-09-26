using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Resources;
using MackySoft.Navigathena.Runtime.Screens;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Transitions
{
    internal sealed class TransitionPlayback
    {
        private static TransitionTargetsContext CreateTargets (PresentationUpdate update) => new(update.Changes.Select(change => new TransitionScreenChange(
            (change.AfterEntry ?? change.BeforeEntry)!.Id, (change.AfterEntry ?? change.BeforeEntry)!.Route,
            change.BeforeParticipation?.OutputPresented == true, change.AfterParticipation?.OutputPresented == true)).ToArray());

        private readonly IScreenRuntimeServices runtime;
        private readonly PresentationTransition transition;
        private readonly ResourceScope resources;
        private readonly ManagedTransitionPreparationContext preparation;
        private readonly TaskCompletionSource<object?> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private INavigationTransitionEffect? effect;
        private ScreenInstance? screenOwner;
        private IDisposable? screenUsage;
        private ViewRegistration? screenView;
        private Task? finalization;
        private bool begun;

        public TransitionPlayback (IScreenRuntimeServices runtime, PresentationTransition transition, NavigationTransition configuration, ViewRegistry views)
        {
            this.runtime = runtime;
            this.transition = transition;
            Configuration = configuration;
            resources = new ResourceScope(() => new ValueTask(ended.Task), _ => resources!.RequestEndAsync());
            preparation = new ManagedTransitionPreparationContext(resources, views);
        }

        public NavigationTransition Configuration
        {
            get;
        }
        public NavigationOperationId OperationId => transition.OperationId;
        internal NavigationOperationKind Operation => transition.Operation;
        internal ReloadOptions? ReloadOptions => transition.Options?.Reload;
        internal bool WaitForTermination => transition.WaitForTermination || transition.Operation == NavigationOperationKind.Reload;
        internal RegionInstanceId? TargetRegion => transition.TargetRegion;
        internal BackOptions? BackOptions => transition.Options?.Back;
        internal CallChange? CallChange => transition.CallChange;
        public bool HasEnded => ended.Task.IsCompletedSuccessfully;
        public bool FinalizationStarted => finalization is not null;
        public bool DestinationCommitted
        {
            get; set;
        }
        public bool Departed
        {
            get; set;
        }
        public Task Ended => ended.Task;
        public bool Retains (ScreenInstance screen) => !HasEnded && ReferenceEquals(screenOwner, screen);
        public void BeginRebinding () => transition.ActiveOperation?.ApplyCommit(() =>
{
});

        public async ValueTask BeginAsync (bool destinationPrepared, CancellationToken cancellationToken)
        {
            if (begun || Configuration.Source == TransitionEffectSource.None || (Configuration.Source == TransitionEffectSource.DestinationScreen) != destinationPrepared)
            {
                return;
            }

            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, resources.EndingToken);
            begun = true;
            if (Configuration.Source == TransitionEffectSource.Factory)
            {
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    effect = await Configuration.CreateEffectAsync!(preparation, cancellation.Token)
                        ?? throw new NavigationConfigurationException("The transition factory returned no effect.");
                }
                finally
                {
                    await resources.CloseAsync();
                }
            }
            else
            {
                NavigationState state = destinationPrepared ? transition.ProposedAfter : transition.Before;
                RegionInstanceId target = transition.TargetRegion ?? throw new NavigationConfigurationException("A screen-owned transition requires a target region.");
                RegionState region = state.GetRegion(target);
                if (region.Entries.Count == 0)
                {
                    throw new NavigationConfigurationException("The selected transition endpoint has no screen.");
                }

                screenOwner = runtime.Find(state.GetPresentation(region.Entries[region.Entries.Count - 1]))
                    ?? throw new NavigationConfigurationException("The transition's owning screen is unavailable.");
                screenUsage = screenOwner.Use();
                effect = screenOwner.Creation.TransitionEffect
                    ?? throw new NavigationConfigurationException("The selected screen did not register a transition effect.");
                screenView = screenOwner.Creation.TransitionView;
            }

            cancellation.Token.ThrowIfCancellationRequested();
            runtime.AddTransitionViews(this, Views().ToArray());
            foreach (ViewRegistration view in Views())
            {
                view.Apply(new ViewPresentation(true, false, view.Order));
            }

            cancellation.Token.ThrowIfCancellationRequested();
            await effect.BeginAsync(new TransitionBeginContext(transition.Operation, transition.Before, transition.ProposedAfter), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
        }

        public async ValueTask PrepareSwitchAsync (PresentationUpdate update, CancellationToken cancellationToken)
        {
            if (effect is not null)
            {
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, resources.EndingToken);
                cancellation.Token.ThrowIfCancellationRequested();
                await effect.PrepareSwitchAsync(CreateTargets(update), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
            }

            resources.EndingToken.ThrowIfCancellationRequested();
        }

        public async ValueTask AfterCommitAsync (PresentationUpdate update, CancellationToken cancellationToken)
        {
            if (effect is not null)
            {
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, resources.EndingToken);
                cancellation.Token.ThrowIfCancellationRequested();
                await effect.AfterCommitAsync(CreateTargets(update), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
            }
        }

        public ValueTask FinishAsync (TransitionSettlementTarget target, bool destinationCommitted) => new(finalization ??= runtime.Terminations.Track(NavigationTerminationKind.Transition, screenOwner?.Entry.Id, screenOwner?.Id, OperationId, () => new ValueTask(FinishCoreAsync(target, destinationCommitted))));

        private async Task FinishCoreAsync (TransitionSettlementTarget target, bool destinationCommitted)
        {
            try
            {
                if (effect is not null)
                {
                    await effect.SettleAsync(new TransitionSettlementContext(target, destinationCommitted), CancellationToken.None);
                    if (Configuration.Source == TransitionEffectSource.Factory)
                    {
                        effect = null;
                    }
                }

                if (screenView is not null)
                {
                    screenView.Apply(new ViewPresentation(false, false, screenView.Original.Order));
                }

                foreach (ViewRegistration view in preparation.Registrations)
                {
                    view.Release();
                }
                runtime.RemoveTransitionViews(this);

                await resources.DisposeAsync();
                screenUsage?.Dispose();
                screenUsage = null;
                ended.TrySetResult(null);
            }
            catch (Exception exception)
            {
                // Keep screen usage and borrowed resources when the effect has not stopped safely.
                ended.TrySetException(exception);
                throw;
            }
        }

        private IEnumerable<ViewRegistration> Views ()
        {
            if (screenView is not null)
            {
                yield return screenView;
            }

            foreach (ViewRegistration view in preparation.Registrations)
            {
                yield return view;
            }
        }
    }
}
