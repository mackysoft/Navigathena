using System;

namespace MackySoft.Navigathena
{
    /// <summary> Applies runtime output, native focus, and user-input permissions to one concrete view. </summary>
    /// <remarks>Identity identifies the native view, even when multiple adapters wrap it. OrderingDomain identifies views whose numeric orders can be compared. Applying permissions must not replace the game's styling or animation state.</remarks>
    public interface IViewAdapter
    {
        object Identity { get; }
        object OrderingDomain { get; }
        bool IsAlive { get; }
        ViewPresentation Presentation { get; }
        event Action<string>? Lost;
        /// <summary>Checks native presentation constraints without changing the view.</summary>
        void Validate (ViewPresentation presentation);
        /// <summary> Applies a synchronous presentation change without changing semantic control availability. </summary>
        /// <remarks> For IViewPresentationBatchAdapter, route permission changes through its PresentationBatch; synchronous Apply is reserved for initial closed state and order-only changes. </remarks>
        void Apply (ViewPresentation presentation);
    }
}
