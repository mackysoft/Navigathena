using System;

namespace MackySoft.Navigathena.SceneManagement.Unsafe
{
	public interface IUnsafeSceneNavigator
	{

		/// <summary>
		/// Get <see cref="ISceneHistoryBuilder"/> to edit scene history directly.
		/// </summary>
		ISceneHistoryBuilder GetHistoryBuilderUnsafe ();
	}

	public static class UnsafeSceneNavigatorExtensions
	{
		public static ISceneHistoryBuilder GetHistoryBuilderUnsafe (this ISceneNavigator navigator)
		{
			return navigator is IUnsafeSceneNavigator unsafeNavigator ? unsafeNavigator.GetHistoryBuilderUnsafe() : throw new NotSupportedException($"{navigator.GetType().Name} does not support {nameof(IUnsafeSceneNavigator)}.");
		}
	}
}