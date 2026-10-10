using System.Threading;
using System.Threading.Tasks;

#if NAVIGATHENA_UNITASK
using FrameAwaitable = Cysharp.Threading.Tasks.UniTask;
#elif UNITY_2023_1_OR_NEWER
using FrameAwaitable = UnityEngine.Awaitable;
#else
#error Navigathena requires UniTask 2.1 or later on Unity versions before 2023.1. Install com.cysharp.unitask through Unity Package Manager.
#endif

namespace MackySoft.Navigathena.Unity.NativeResources
{
    internal static class UnityFrame
    {
        // Adapter assemblies depend on this stable boundary, not the optional awaitable's assembly.
        internal static async ValueTask WaitNextAsync (CancellationToken cancellationToken) => await NextAsync(cancellationToken);

        // Return the selected awaitable directly, without another async state machine for each frame.
        internal static FrameAwaitable NextAsync (CancellationToken cancellationToken)
        {
#if NAVIGATHENA_UNITASK
            return Cysharp.Threading.Tasks.UniTask.NextFrame(cancellationToken: cancellationToken);
#else
            return UnityEngine.Awaitable.NextFrameAsync(cancellationToken);
#endif
        }
    }
}
