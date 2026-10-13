using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary>Fences a native dispatch queue without polling or waiting a fixed number of frames.</summary>
    internal sealed class NativeDispatchCheckpoint : EventBase<NativeDispatchCheckpoint>
    {
        public TaskCompletionSource<object?>? Completion { get; set; }

        protected override void Init ()
        {
            base.Init();
            Completion = null;
        }

        protected override void PostDispatch (IPanel panel)
        {
            base.PostDispatch(panel);
            // A queued checkpoint resumes through Unity's synchronization context, after
            // the dispatcher's remaining events have drained, rather than inside this callback.
            Completion?.TrySetResult(null);
        }
    }
}
