using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MackySoft.Navigathena.Samples.Startup
{
    public sealed class StartupRevealEffect : INavigationTransitionEffect
    {
        private readonly StartupOverlay overlay;
        public StartupRevealEffect (StartupOverlay overlay) => this.overlay = overlay;
        public ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken)
        {
            overlay.Opacity = 1;
            return default;
        }
        public ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;
        public ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;

        public async ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken)
        {
            if (context.Target != TransitionSettlementTarget.Destination)
            {
                overlay.Opacity = 1;
                return;
            }
            float elapsed = 0;
            while (elapsed < 0.3f)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed += Time.unscaledDeltaTime;
                overlay.Opacity = 1 - Mathf.Clamp01(elapsed / 0.3f);
                await UniTask.NextFrame(cancellationToken: cancellationToken);
            }
            overlay.Opacity = 0;
        }
    }
}
