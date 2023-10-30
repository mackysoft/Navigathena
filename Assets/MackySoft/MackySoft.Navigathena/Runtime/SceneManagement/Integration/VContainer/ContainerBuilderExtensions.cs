#if ENABLE_NAVIGATHENA_VCONTAINER
using System.Runtime.CompilerServices;
using VContainer;

namespace MackySoft.Navigathena.SceneManagement.VContainer
{
	public static class ContainerBuilderExtensions
	{

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static RegistrationBuilder RegisterSceneLifecycle<TSceneLifecycle> (this IContainerBuilder builder) where TSceneLifecycle : ISceneLifecycle
		{
			return builder.Register<ISceneLifecycle, TSceneLifecycle>(Lifetime.Singleton);
		}
	}
}
#endif