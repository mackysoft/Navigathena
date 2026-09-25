using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal abstract class CallRecord
    {
        protected CallRecord (NavigationEntryId? owner, PresentationId? ownerPresentation, RegionInstanceId region, Guid? parent)
        {
            Owner = owner;
            OwnerPresentation = ownerPresentation;
            Region = region;
            Parent = parent;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public NavigationEntryId? Owner
        {
            get;
        }
        public PresentationId? OwnerPresentation
        {
            get;
        }
        public RegionInstanceId Region
        {
            get;
        }
        public Guid? Parent
        {
            get;
        }
        public bool Published
        {
            get; set;
        }
        public bool Ending
        {
            get; set;
        }
        public bool Closing
        {
            get; set;
        }
        public CallEndReason? EndReason
        {
            get; set;
        }
        public bool Answered => EndReason == CallEndReason.Answer;
        public bool Discarded
        {
            get; set;
        }
        public List<Task> Retirements { get; } = new();
        public NavigationOperation? Opening
        {
            get; set;
        }
        public TaskCompletionSource<NavigationResult> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation
        {
            get; set;
        }
        public abstract Task Completion
        {
            get;
        }
        public abstract void Finish (NavigationState finalSnapshot);
        public abstract void CancelOwner ();
        public abstract void Fail (Exception exception);
    }

    internal sealed class CallRecord<TResult> : CallRecord
    {
        private readonly TaskCompletionSource<TResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool RequiresAnswer
        {
            get;
        }

        public CallRecord (NavigationEntryId? owner, PresentationId? presentation, RegionInstanceId region, Guid? parent, bool requiresAnswer)
            : base(owner, presentation, region, parent)
        {
            RequiresAnswer = requiresAnswer;
            // A caller may end before it observes the call's completion.
            _ = completion.Task.ContinueWith(task =>
            {
                _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = Opened.Task.ContinueWith(task =>
            {
                _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public TResult Value { get; set; } = default!;
        public override Task Completion => completion.Task;
        public Task<TResult> Answer => completion.Task;

        public override void Finish (NavigationState finalSnapshot)
        {
            if (Discarded || EndReason == CallEndReason.Cancellation || EndReason == CallEndReason.Interrupted)
            {
                completion.TrySetCanceled();
            }
            else
            {
                switch (EndReason)
                {
                    case CallEndReason.Answer:
                        completion.TrySetResult(Value);
                        break;
                    case CallEndReason.Back:
                    case CallEndReason.Close:
                        if (RequiresAnswer)
                        {
                            completion.TrySetCanceled();
                        }
                        else
                        {
                            completion.TrySetResult(Value);
                        }
                        break;
                    default:
                        Fail(new ScreenCallException(Id, ScreenCallFailureStage.Returning, Answered, finalSnapshot,
                            new InvalidOperationException("The screen call was removed by an unrelated operation.")));
                        break;
                }
            }
        }

        public override void CancelOwner ()
        {
            Discarded = true;
            completion.TrySetCanceled();
        }

        public override void Fail (Exception exception)
        {
            if (exception is OperationCanceledException cancellation)
            {
                completion.TrySetCanceled(cancellation.CancellationToken);
            }
            else
            {
                completion.TrySetException(exception);
            }
        }
    }
}
