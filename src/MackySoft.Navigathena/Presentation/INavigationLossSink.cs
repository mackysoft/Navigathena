using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Accepts a passive report containing the complete presentation group lost in one physical event.</summary>
    public interface INavigationLossSink
    {
        /// <summary>Reports a physical-loss group without allowing caller cancellation to withdraw an accepted report.</summary>
        /// <param name="loss">The complete group of logical entries and physical generations lost in one physical event.</param>
        /// <param name="cancellationToken">Cancels only this caller's wait for the result.</param>
        /// <returns>The applied, obsolete, or closed result when it completes before caller cancellation.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="loss"/> is <see langword="null"/>.</exception>
        ValueTask<PresentationLossResult> ReportAsync (PresentationLoss loss, CancellationToken cancellationToken = default);
    }

}
