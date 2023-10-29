using System;

namespace MackySoft.Navigathena.SceneManagement
{
	public sealed class TypedDataSceneIdentifier<T> : ISceneIdentifier<T> where T : ISceneData
	{

		readonly ISceneIdentifier m_Inner;

		public TypedDataSceneIdentifier (ISceneIdentifier inner)
		{
			m_Inner = inner ?? throw new ArgumentNullException(nameof(inner));
		}

		public ISceneHandle CreateHandle() => m_Inner.CreateHandle();

	}
}