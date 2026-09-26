using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Execution;

namespace MackySoft.Navigathena.Hosting
{

    /// <summary>Creates and owns the Core runtime for one independently controlled navigation world.</summary>
    /// <remarks>
    /// The application owner chooses the host lifetime; no scene or application-wide lifetime is required.
    /// Resetting the root region keeps the host alive. Await shutdown before replacing the host or
    /// releasing its external dependencies. A shut-down host cannot be restarted.
    /// </remarks>
    public sealed class NavigationHost : INavigationHost
    {
        private readonly NavigationRuntime runtime;
        private readonly ScreenRuntime? screens;
        private readonly object shutdownSync = new();
        private Task? shutdown;
        private int starting;

        private NavigationHost (NavigationRuntime runtime, ScreenRuntime? screens = null)
        {
            this.runtime = runtime;
            this.screens = screens;
            Client = runtime;
            State = runtime;
            Recovery = runtime;
            Loss = runtime;
            HostIncidents = runtime;
        }

        public INavigationClient Client
        {
            get;
        }
        public INavigationStateSource State
        {
            get;
        }
        public INavigationRecoveryClient Recovery
        {
            get;
        }
        public INavigationLossSink Loss
        {
            get;
        }
        public INavigationHostIncidentSink HostIncidents
        {
            get;
        }
        public INavigationTerminationSource Terminations => screens?.Terminations ?? throw new InvalidOperationException("The protocol test host does not own screen resources.");
        public RegionInstanceId Root => runtime.Root;

        /// <summary>Creates a resource-free host. Asynchronous callbacks preserve the ambient execution environment.</summary>
        public static NavigationHost Create (ScreenCatalog catalog, NavigationHostOptions? options = null)
        {
            if (catalog is null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            NavigationHostOptions configuration = options ?? new NavigationHostOptions();
            ScreenRuntime screens = new(catalog, configuration);
            NavigationRuntime runtime = new(catalog.Definition, screens, configuration.CommitObservers, result =>
            {
                configuration.OperationCompleted?.Invoke(result);
                return default;
            });
            screens.Bind(runtime, runtime, runtime);
            return new NavigationHost(runtime, screens);
        }

        /// <summary>Performs the first root navigation using the same screen lifecycle as later requests.</summary>
        public Task StartAsync<TRoute> (TRoute route, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route => NavigationOperation.ExecuteAsync(() => Start(route, options), cancellationToken);

        /// <summary>Performs the first root navigation with its initial child configuration.</summary>
        public Task StartAsync<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route => NavigationOperation.ExecuteAsync(() => Start(destination, options), cancellationToken);

        public NavigationOperation Start<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => Start(Destination.For(route), options);

        public NavigationOperation Start<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route
        {
            if (State.Current.GetRegion(Root).Entries.Count != 0 || Interlocked.CompareExchange(ref starting, 1, 0) != 0)
            {
                throw new InvalidOperationException("The host has already started or is starting.");
            }

            NavigationOperation operation;
            try
            {
                operation = Client.Reset(Root, destination, options);
            }
            catch
            {
                Volatile.Write(ref starting, 0);
                throw;
            }
            _ = FinishStartingAsync(operation);
            return operation;
        }

        private async Task FinishStartingAsync (NavigationOperation operation)
        {
            bool committed = false;
            try
            {
                committed = (await operation.WaitAsync()).DestinationCommitted;
            }
            catch (NavigationException exception)
            {
                committed = exception.DestinationCommitted;
            }
            catch
            {
                // The operation retains the error for all result observers.
            }
            finally
            {
                Volatile.Write(ref starting, committed ? 2 : 0);
            }
        }

        internal static NavigationHost Create (NavigationDefinition definition, IPresentationRealizer realizer, NavigationHostOptions? options = null)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (realizer is null)
            {
                throw new ArgumentNullException(nameof(realizer));
            }

            NavigationHostOptions configuration = options ?? new NavigationHostOptions();
            return new NavigationHost(new NavigationRuntime(definition, realizer, configuration.CommitObservers));
        }

        public ValueTask ShutdownAsync ()
        {
            TaskCompletionSource<object?> completion;
            lock (shutdownSync)
            {
                if (shutdown is not null)
                {
                    return new ValueTask(shutdown);
                }

                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdown = completion.Task;
            }

            _ = FinishShutdownAsync(completion);
            return new ValueTask(completion.Task);
        }

        private async Task FinishShutdownAsync (TaskCompletionSource<object?> completion)
        {
            try
            {
                await ShutdownCoreAsync();
                completion.SetResult(null);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }

        private async Task ShutdownCoreAsync ()
        {
            await runtime.DisposeAsync();
            if (screens is not null)
            {
                await screens.ShutdownAsync();
            }
        }

        public ValueTask DisposeAsync () => ShutdownAsync();
    }

}
