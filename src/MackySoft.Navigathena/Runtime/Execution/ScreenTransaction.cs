using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Transitions;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal sealed class ScreenTransaction : IPresentationTransaction
    {
        private readonly ScreenRuntime runtime;
        private readonly TransitionPlayback playback;
        public ScreenTransaction (ScreenRuntime runtime, TransitionPlayback playback, IReadOnlyList<NavigationEntryId> departures)
        {
            this.runtime = runtime;
            this.playback = playback;
            DepartureEntries = departures;
        }
        public IReadOnlyList<NavigationEntryId> DepartureEntries
        {
            get;
        }

        public async ValueTask<IPreparedPublication> PrepareAsync (PresentationUpdate update, CancellationToken cancellationToken)
        {
            PreparedScreenPublication publication = new(runtime, update, playback);
            try
            {
                await publication.PrepareAsync(cancellationToken);
                return (IPreparedPublication)publication;
            }
            catch (Exception preparationFailure)
            {
                try
                {
                    await publication.DisposeAsync();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Screen preparation and its rollback failed.", preparationFailure, cleanupFailure);
                }
                throw;
            }
        }

        public async ValueTask DisposeAsync ()
        {
            try
            {
                if (!playback.FinalizationStarted)
                {
                    await playback.FinishAsync(playback.DestinationCommitted || playback.Departed ? TransitionSettlementTarget.Unavailable : TransitionSettlementTarget.Source, playback.DestinationCommitted);
                }
            }
            finally
            {
                runtime.Release(playback);
            }
        }
    }
}
