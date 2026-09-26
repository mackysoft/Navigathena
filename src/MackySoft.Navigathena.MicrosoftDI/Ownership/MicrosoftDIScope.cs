using System;
using System.Linq;
using System.Threading.Tasks;
using MackySoft.Navigathena.MicrosoftDI.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace MackySoft.Navigathena.MicrosoftDI.Ownership
{
    internal sealed class MicrosoftDIScope : IAsyncDisposable
    {
        private ServiceProvider? provider;
        private AsyncServiceScope? scope;
        private Task? disposal;
        public ScreenServiceRegistration[] Roles { get; private set; } = Array.Empty<ScreenServiceRegistration>();
        public IServiceProvider Services => scope?.ServiceProvider ?? throw new InvalidOperationException("The screen scope has not been built.");

        public void Build (Action<IServiceCollection> configure)
        {
            ServiceCollection registrations = new();
            configure(registrations);
            Roles = registrations.Select(item => item.ImplementationInstance).OfType<ScreenServiceRegistration>().ToArray();
            foreach (ScreenServiceRegistration role in Roles)
            {
                ServiceDescriptor[] services = registrations.Where(item => item.ServiceType == role.Type).ToArray();
                if (services.Length != 1 || services[0].Lifetime == ServiceLifetime.Transient)
                {
                    throw new NavigationConfigurationException("A screen participant must have one scoped, singleton, or existing-instance registration: " + role.Type);
                }
            }
            if (registrations.Any(item => typeof(Route).IsAssignableFrom(item.ServiceType)))
            {
                throw new NavigationConfigurationException("Navigation input belongs to lifecycle arguments, not screen DI registrations.");
            }
            provider = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            scope = provider.CreateAsyncScope();
        }

        public ValueTask DisposeAsync () => new(disposal ??= DisposeCoreAsync());

        private async Task DisposeCoreAsync ()
        {
            if (scope is AsyncServiceScope execution)
            {
                await execution.DisposeAsync();
                scope = null;
            }
            if (provider is not null)
            {
                await provider.DisposeAsync();
                provider = null;
            }
        }
    }
}
