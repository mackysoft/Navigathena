using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary>Acquires UI Toolkit views before exposing their native input scope.</summary>
    public static class UiToolkitScreenCreationExtensions
    {
        /// <summary>Acquires a configured screen prefab and returns its screen-implementation interface.</summary>
        /// <remarks>The prefab requires ScreenPresentation. The instance and its panel are released before the screen lifetime ends.</remarks>
        public static async ValueTask<IUiToolkitView> AcquireUiToolkitScreenAsync (this ScreenCreationContext creation,
            UiToolkitViewAdapter prefab, UiToolkitPresentationHost host, CancellationToken cancellationToken = default)
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            UiToolkitViewAdapter view = await creation.Lifetime.AcquireUiToolkitViewAsync(prefab, host, cancellationToken);
            creation.ConnectScreen(view);
            return view.View;
        }

        /// <summary>Acquires an input-closed view for composition, including pointer blockers and transition output.</summary>
        /// <remarks>Register the adapter with the corresponding preparation context; pass only its View interface to screen code.</remarks>
        public static ValueTask<UiToolkitViewAdapter> AcquireUiToolkitViewAsync (this LifetimeContext lifetime,
            UiToolkitViewAdapter prefab, UiToolkitPresentationHost host, CancellationToken cancellationToken = default)
        {
            if (lifetime is null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }
            return lifetime.AcquireAsync(new UiToolkitPrefabAcquisition(prefab, host), cancellationToken);
        }
    }
}
