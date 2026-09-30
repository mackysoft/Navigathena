using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using VContainer;

namespace MackySoft.Navigathena.VContainer
{
    public static class ScreenContainerBuilderExtensions
    {
        private static readonly ConditionalWeakTable<IContainerBuilder, ScreenServiceRoles> Roles = new();

        public static void RegisterScreenLifecycleHandler<T> (this IContainerBuilder builder) where T : class
            => Register<T>(builder, ScreenServiceRole.Lifecycle);

        internal static ScreenServiceRoles GetRoles (IContainerBuilder builder) => Roles.GetValue(builder, _ => new ScreenServiceRoles());

        private static void Register<T> (IContainerBuilder builder, ScreenServiceRole role) where T : class
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            ScreenServiceRoles roles = GetRoles(builder);
            if (role == ScreenServiceRole.Lifecycle && roles.Items.Any(item => item.Role == ScreenServiceRole.Lifecycle))
            {
                throw new NavigationConfigurationException("A screen scope can specify only one lifecycle handler.");
            }
            if (roles.Items.Any(item => item.Type == typeof(T) && item.Role == role))
            {
                throw new NavigationConfigurationException("The screen service role is already registered: " + typeof(T));
            }
            if (!builder.Exists(typeof(T)))
            {
                builder.Register<T>(Lifetime.Scoped);
            }
            roles.Items.Add((typeof(T), role));
        }
    }

    internal enum ScreenServiceRole
    {
        Lifecycle
    }

    internal sealed class ScreenServiceRoles
    {
        public List<(Type Type, ScreenServiceRole Role)> Items { get; } = new();
    }
}
