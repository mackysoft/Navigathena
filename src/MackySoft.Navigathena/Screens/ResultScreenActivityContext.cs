using System;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    public sealed class ScreenActivityContext<TResult> : ScreenActivityContext
    {
        internal ScreenActivityContext (ScreenActivityContext context)
            : base(context.EntryId, context.RegionId, context.Navigation, context.CancellationToken, context.Calls, context.StartWork, context.IsFirstActivation, context.Reason, context.PreparationReason)
        {
            Call = context.Calls.Connect<TResult>();
        }

        public ScreenCall<TResult> Call
        {
            get;
        }

        public ScreenWork StartWork (Func<ScreenWorkContext<TResult>, ValueTask> work)
        {
            if (work is null)
            {
                throw new ArgumentNullException(nameof(work));
            }
            return base.StartWork(context => work(new ScreenWorkContext<TResult>(context)));
        }
    }
}
