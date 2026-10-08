using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.Unity
{
    /// <summary>Loads a scene from the build's scene list additively and unloads that instance after its managed users stop.</summary>
    /// <remarks>Use the full asset path. Do not concurrently load the same path through an unmanaged loader.
    /// Unity cannot cancel an in-flight scene load; cancellation waits for loading to finish so ownership can be released safely.</remarks>
    public sealed class SceneAcquisition : IResourceAcquisition<Scene>
    {
        // Unity returns an AsyncOperation, not a scene handle. Serialize native loads while the sceneLoaded event identifies the acquired instance.
        private static bool loading;
        private readonly string scenePath;
        private Scene scene;
        private ResourceAcquisitionContext? context;
        private bool started;

        /// <summary>Creates an acquisition for one additional instance of the specified scene.</summary>
        /// <param name="scenePath">The full asset path of a scene enabled in the build's scene list.</param>
        /// <exception cref="ArgumentException">The path is empty or is not a scene asset path.</exception>
        public SceneAcquisition (string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath) || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || !scenePath.Contains("/"))
            {
                throw new ArgumentException("Use the full scene asset path, including the .unity extension.", nameof(scenePath));
            }
            this.scenePath = scenePath;
        }

        public async ValueTask<Scene> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            UnityThread.AssertCurrent();
            if (started)
            {
                throw new InvalidOperationException("A scene acquisition can only be started once.");
            }
            started = true;
            // Scene acquisition runs on the Unity thread. Yield competing loads through
            // that thread's player loop without a managed thread-pool dependency.
            while (loading)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UnityFrame.NextAsync(CancellationToken.None);
            }
            cancellationToken.ThrowIfCancellationRequested();
            loading = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                int index = SceneUtility.GetBuildIndexByScenePath(scenePath);
                if (index < 0)
                {
                    throw new NavigationConfigurationException("The scene is not enabled in the build's scene list: " + scenePath);
                }
                this.context = context;
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                AsyncOperation operation = SceneManager.LoadSceneAsync(index, LoadSceneMode.Additive)
                    ?? throw new InvalidOperationException("Unity did not start the scene load: " + scenePath);
                IProgress<SceneProgress> progress = context.Progress.GetReporter(UnityProgress.SceneLoading);
                while (!operation.isDone)
                {
                    progress.Report(new SceneProgress(scenePath, operation.progress, operation.isDone));
                    await UnityFrame.NextAsync(CancellationToken.None);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    throw new InvalidOperationException("Unity completed without the acquired scene: " + scenePath);
                }
                progress.Report(new SceneProgress(scenePath, 1, true));
                return scene;
            }
            finally
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                loading = false;
            }
        }

        public async ValueTask ReleaseAsync (NavigationProgressReporter progress)
        {
            UnityThread.AssertCurrent();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            context = null;
            if (scene.IsValid() && scene.isLoaded)
            {
                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene)
                    ?? throw new InvalidOperationException("Unity did not start unloading the acquired scene: " + scenePath);
                IProgress<SceneProgress> reports = progress.GetReporter(UnityProgress.SceneUnloading);
                while (!operation.isDone)
                {
                    reports.Report(new SceneProgress(scenePath, operation.progress, operation.isDone));
                    await UnityFrame.NextAsync(CancellationToken.None);
                }
                if (scene.IsValid() && scene.isLoaded)
                {
                    throw new InvalidOperationException("The acquired scene remains loaded: " + scenePath);
                }
                reports.Report(new SceneProgress(scenePath, 1, true));
            }
            scene = default;
        }

        private void OnSceneLoaded (Scene loaded, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && string.Equals(loaded.path, scenePath, StringComparison.OrdinalIgnoreCase))
            {
                scene = loaded;
            }
        }

        private void OnSceneUnloaded (Scene unloaded)
        {
            if (unloaded == scene)
            {
                context?.ReportLoss("The acquired scene was unloaded outside its managed lifetime.");
            }
        }
    }
}
