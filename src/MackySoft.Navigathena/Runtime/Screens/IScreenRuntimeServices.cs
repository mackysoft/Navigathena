using System.Collections.Generic;
using MackySoft.Navigathena.Runtime.Lifetimes;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Screens
{
    internal interface IScreenRuntimeServices
    {
        ScreenCatalog Catalog
        {
            get;
        }
        TerminationJournal Terminations
        {
            get;
        }
        ScreenInstance? Find (PresentationState? presentation);
        void AddTransitionViews (object transition, IReadOnlyList<ViewRegistration> views);
        void RemoveTransitionViews (object transition);
        void ReportEquipmentFailure (string reason);
    }
}
