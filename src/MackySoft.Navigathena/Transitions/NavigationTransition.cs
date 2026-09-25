using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Describes how an operation obtains its whole-transition effect.</summary>
    /// <remarks>The runtime checks cancellation before invoking the effect factory and before using its result. The factory forwards the token to ongoing work without duplicating the entry check. Registered resources and a returned effect are cleaned up even after cancellation.</remarks>
    public sealed class NavigationTransition
    {
        public NavigationTransition (NavigationTransitionScope scope, Func<NavigationTransitionPreparationContext, CancellationToken, ValueTask<INavigationTransitionEffect>> createEffectAsync, bool requiresSimultaneousScreens = false)
            : this(scope, TransitionEffectSource.Factory)
        {
            CreateEffectAsync = createEffectAsync ?? throw new ArgumentNullException(nameof(createEffectAsync));
            RequiresSimultaneousScreens = requiresSimultaneousScreens;
        }

        private NavigationTransition (NavigationTransitionScope scope, TransitionEffectSource source)
        {
            if (!Enum.IsDefined(typeof(NavigationTransitionScope), scope))
            {
                throw new ArgumentOutOfRangeException(nameof(scope));
            }

            Scope = scope;
            Source = source;
        }

        public NavigationTransitionScope Scope
        {
            get;
        }
        public bool RequiresSimultaneousScreens
        {
            get;
        }
        public static NavigationTransition None { get; } = new(NavigationTransitionScope.Region, TransitionEffectSource.None);
        public static NavigationTransition FromSourceScreen (NavigationTransitionScope scope) => new(scope, TransitionEffectSource.SourceScreen);
        public static NavigationTransition FromDestinationScreen (NavigationTransitionScope scope) => new(scope, TransitionEffectSource.DestinationScreen);
        internal TransitionEffectSource Source
        {
            get;
        }
        internal Func<NavigationTransitionPreparationContext, CancellationToken, ValueTask<INavigationTransitionEffect>>? CreateEffectAsync
        {
            get;
        }
    }
}
