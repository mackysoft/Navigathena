using System.Threading;

namespace MackySoft.Navigathena
{
    /// <summary>Navigation belongs to the live history binding, not to the activity that started this work.</summary>
    public class ScreenWorkContext
    {
        internal IScreenCallScope Calls
        {
            get;
        }

        internal ScreenWorkContext (NavigationEntryId entryId, IScreenNavigation navigation, CancellationToken cancellationToken, IScreenCallScope calls)
        {
            EntryId = entryId;
            Navigation = navigation;
            CancellationToken = cancellationToken;
            Calls = calls;
        }

        public NavigationEntryId EntryId
        {
            get;
        }
        public IScreenNavigation Navigation
        {
            get;
        }
        public CancellationToken CancellationToken
        {
            get;
        }

    }

    public sealed class ScreenWorkContext<TResult> : ScreenWorkContext
    {
        internal ScreenWorkContext (ScreenWorkContext context)
            : base(context.EntryId, context.Navigation, context.CancellationToken, context.Calls)
        {
            Call = context.Calls.Connect<TResult>();
        }

        public ScreenCall<TResult> Call
        {
            get;
        }
    }
}
