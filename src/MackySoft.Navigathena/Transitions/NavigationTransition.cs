using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Runtime.Transitions;

namespace MackySoft.Navigathena
{
    /// <summary>Describes how an operation obtains its whole-transition effect.</summary>
    /// <remarks>The runtime checks cancellation before invoking the effect factory and before using its result. The factory forwards the token to ongoing work without duplicating the entry check. Registered resources and a returned effect are cleaned up even after cancellation.</remarks>
    public sealed class NavigationTransition
    {
        /// <summary>Defines an operation-owned effect and when it must settle relative to retired screen resources.</summary>
        /// <param name="scope">The region or host whose presentation and input the effect covers.</param>
        /// <param name="createEffectAsync">Creates or borrows the effect and registers its dependencies with the supplied lifetime. With AfterResourceRelease, their external owners must outlive settlement and must not end from a retiring screen's cleanup.</param>
        /// <param name="requiresSimultaneousScreens">Requires outgoing and incoming screen instances to coexist during the effect.</param>
        /// <param name="endTiming">AfterResourceRelease keeps the effect alive and subscribed to progress until retired screen cleanup completes. Activation always follows settlement.</param>
        public NavigationTransition (NavigationTransitionScope scope, Func<NavigationTransitionPreparationContext, CancellationToken, ValueTask<INavigationTransitionEffect>> createEffectAsync, bool requiresSimultaneousScreens = false, TransitionEndTiming endTiming = TransitionEndTiming.AfterPresentation)
            : this(scope, TransitionEffectSource.Factory, endTiming)
        {
            CreateEffectAsync = createEffectAsync ?? throw new ArgumentNullException(nameof(createEffectAsync));
            RequiresSimultaneousScreens = requiresSimultaneousScreens;
        }

        /// <summary>Connects an operation-local typed state before constructing the loading effect or acquiring destination screens.</summary>
        /// <param name="scope">The region or host covered by the effect.</param>
        /// <param name="progress">The application's immutable state definition. Each execution receives a fresh state.</param>
        /// <param name="prepare">Acquires or borrows the display and binds the supplied source with ObserveProgress. It does not perform destination initialization.</param>
        /// <param name="requiresSimultaneousScreens">Whether outgoing and incoming screen instances must coexist.</param>
        /// <param name="endTiming">Whether settlement also waits for retired screen resources to be released.</param>
        public static NavigationTransition Create<TState> (NavigationTransitionScope scope, ProgressDefinition<TState> progress, Func<NavigationTransitionPreparationContext, ProgressSource<TState>, CancellationToken, ValueTask<INavigationTransitionEffect>> prepare, bool requiresSimultaneousScreens = false, TransitionEndTiming endTiming = TransitionEndTiming.AfterPresentation)
        {
            if (progress is null)
            {
                throw new ArgumentNullException(nameof(progress));
            }
            if (prepare is null)
            {
                throw new ArgumentNullException(nameof(prepare));
            }
            return new NavigationTransition(scope, (context, token) => prepare(context, context.CreateProgressSource(progress), token), requiresSimultaneousScreens, endTiming);
        }

        private NavigationTransition (NavigationTransitionScope scope, TransitionEffectSource source, TransitionEndTiming endTiming)
        {
            if (!Enum.IsDefined(typeof(NavigationTransitionScope), scope))
            {
                throw new ArgumentOutOfRangeException(nameof(scope));
            }
            if (!Enum.IsDefined(typeof(TransitionEndTiming), endTiming))
            {
                throw new ArgumentOutOfRangeException(nameof(endTiming));
            }

            Scope = scope;
            Source = source;
            EndTiming = endTiming;
        }

        public NavigationTransitionScope Scope { get; }
        public bool RequiresSimultaneousScreens { get; }
        /// <summary>Determines whether the effect settles before or after retired screen resources finish releasing. Activation always follows settlement.</summary>
        public TransitionEndTiming EndTiming { get; }
        public static NavigationTransition None { get; } = new(NavigationTransitionScope.Region, TransitionEffectSource.None, TransitionEndTiming.AfterPresentation);
        /// <summary>Uses the source screen's effect. AfterResourceRelease requires that screen to remain alive; a transition retiring its owner is rejected before activity stops.</summary>
        public static NavigationTransition FromSourceScreen (NavigationTransitionScope scope, TransitionEndTiming endTiming = TransitionEndTiming.AfterPresentation) => new(scope, TransitionEffectSource.SourceScreen, endTiming);
        /// <summary>Uses the prepared destination's effect. It cannot observe acquisition or initialization preceding its creation and settles before releasing its owner on rollback.</summary>
        public static NavigationTransition FromDestinationScreen (NavigationTransitionScope scope, TransitionEndTiming endTiming = TransitionEndTiming.AfterPresentation) => new(scope, TransitionEffectSource.DestinationScreen, endTiming);
        internal TransitionEffectSource Source { get; }
        internal Func<NavigationTransitionPreparationContext, CancellationToken, ValueTask<INavigationTransitionEffect>>? CreateEffectAsync { get; }
    }
}
