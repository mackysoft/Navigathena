using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Screens
{
    internal interface IScreenActivityRuntime
    {
        INavigationStateSource State
        {
            get;
        }
        bool HasActivated (NavigationEntryId entry);
        bool OwnsCall (NavigationEntryId entry, PresentationId presentation);
        IScreenCallScope BindCall (NavigationEntry entry, PresentationId presentation, Func<bool> isValid);
        void EndBindingCalls (NavigationEntryId entry, PresentationId presentation);
        void ScheduleWork ();
        ValueTask WaitUntilIdleAsync (CancellationToken cancellationToken);
        void ReportEquipmentFailure (string reason);
    }
}
