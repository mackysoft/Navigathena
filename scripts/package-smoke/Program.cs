using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.MicrosoftDI;
using Microsoft.Extensions.DependencyInjection;

internal static class Program
{
    private static async Task Main ()
    {
        foreach (bool useContainer in new[] { false, true })
        {
            List<int> prepared = new();
            int terminated = 0;
            ScreenDefinition<PageRoute> screen = new((creation, _) =>
            {
                Handler handler = new(prepared, () => terminated++);
                if (useContainer)
                {
                    return new(creation.CreateScope(services =>
                    {
                        services.AddScoped(_ => handler);
                        services.AddScreenLifecycleHandler<Handler>();
                    }));
                }
                return new(creation.Resources.CreateOwned(() => handler));
            });
            ScreenCatalog catalog = ScreenCatalog.Build(new RegionDefinitionId("root"), RegionCompositionMode.Layered, screens =>
            {
                screens.Register(screen, route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
                });
            });
            await using NavigationHost host = NavigationHost.Create(catalog);
            await host.StartAsync(new PageRoute(1));
            await host.Client.PushAsync(host.Root, Destination.For(new PageRoute(2)));
            await host.Client.BackAsync(host.Root);
            if (!prepared.SequenceEqual(new[] { 1, 2, 1 }))
            {
                throw new InvalidOperationException("The installed package did not restore the original route.");
            }
            await host.Client.ReplaceFromAsync<PageRoute>(host.Root, new PageRoute(3), new NavigationOptions { RecreateInstance = true });
            await host.ShutdownAsync();
            if (terminated != 2)
            {
                throw new InvalidOperationException("Screen instances did not terminate exactly once.");
            }
        }
        Console.WriteLine("Installed packages: manual and DI navigation, history, restart and shutdown passed.");
    }

    private sealed record PageRoute (int Id) : Route;

    private sealed class Handler : IScreenLifecycleHandler<PageRoute>
    {
        private readonly List<int> prepared;
        private readonly Action terminated;

        public Handler (List<int> prepared, Action terminated)
        {
            this.prepared = prepared;
            this.terminated = terminated;
        }

        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;

        public ValueTask PrepareAsync (PageRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            prepared.Add(route.Id);
            return default;
        }

        public ValueTask ActivateAsync (PageRoute route, ScreenActivityContext activity) => default;
        public ValueTask DeactivateAsync () => default;

        public ValueTask TerminateAsync ()
        {
            terminated();
            return default;
        }
    }
}
