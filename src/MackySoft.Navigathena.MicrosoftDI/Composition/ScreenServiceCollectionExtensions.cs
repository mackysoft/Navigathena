using System;
using System.Linq;
using MackySoft.Navigathena.MicrosoftDI.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace MackySoft.Navigathena.MicrosoftDI
{
    /// <summary>Registers concrete services once and connects their roles without disposable DI aliases.</summary>
    public static class ScreenServiceCollectionExtensions
    {
        public static IServiceCollection AddScreenLifecycleHandler<T> (this IServiceCollection services) where T : class
            => AddRole<T>(services, ScreenServiceRole.Lifecycle);

        /// <summary>Imports an existing service without transferring disposal ownership. End the host before disposing its provider.</summary>
        public static IServiceCollection ImportService<T> (this IServiceCollection services, IServiceProvider provider) where T : class
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (services.Any(item => item.ServiceType == typeof(T)))
            {
                throw new NavigationConfigurationException("The imported service is already registered: " + typeof(T));
            }
            return services.AddSingleton(provider.GetRequiredService<T>());
        }

        private static IServiceCollection AddRole<T> (IServiceCollection services, ScreenServiceRole role) where T : class
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (role == ScreenServiceRole.Lifecycle && services.Select(item => item.ImplementationInstance).OfType<ScreenServiceRegistration>().Any(item => item.Role == ScreenServiceRole.Lifecycle))
            {
                throw new NavigationConfigurationException("A screen scope can specify only one lifecycle handler.");
            }
            if (services.Select(item => item.ImplementationInstance).OfType<ScreenServiceRegistration>().Any(item => item.Type == typeof(T) && item.Role == role))
            {
                throw new NavigationConfigurationException("The screen service role is already registered: " + typeof(T));
            }
            if (!services.Any(item => item.ServiceType == typeof(T)))
            {
                services.AddScoped<T>();
            }
            services.AddSingleton(new ScreenServiceRegistration(typeof(T), role));
            return services;
        }
    }
}
