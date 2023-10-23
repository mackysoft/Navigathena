using System;

namespace MackySoft.Navigathena.SceneManagement
{

	/// <summary>
	/// <para> This is the interface for data used to share data between scenes. </para>
	/// <para> Avoid having large references whenever possible, as this will remain in the history even after the scene is unloaded. </para>
	/// <para> Also, do not have references to GameObjects that exist in the scene. This leads to memory leaks. </para>
	/// </summary>
	public interface ISceneData
	{

	}

	public interface ISceneDataReader
	{
		bool TryRead<T> (out T result) where T : ISceneData;
		ISceneData ReadNonGeneric ();
	}

	public interface ISceneDataWriter
	{
		void Write (ISceneData data);
	}

	public sealed class SceneDataStore
	{

		ISceneData m_Data;

		readonly SceneDataReader m_Reader;
		readonly SceneDataWriter m_Writer;

		public ISceneDataReader Reader => m_Reader;
		public ISceneDataWriter Writer => m_Writer;

		public SceneDataStore ()
		{
			m_Reader = new SceneDataReader(this);
			m_Writer = new SceneDataWriter(this);
		}

		public SceneDataStore (ISceneData data) : this()
		{
			m_Data = data;
		}

		sealed class SceneDataReader : ISceneDataReader
		{

			readonly SceneDataStore m_Store;

			public SceneDataReader (SceneDataStore store)
			{
				m_Store = store;
			}

			public bool TryRead<T> (out T result) where T : ISceneData
			{
				if (m_Store.m_Data is T data)
				{
					result = data;
					return true;
				}
				result = default;
				return false;
			}

			public ISceneData ReadNonGeneric ()
			{
				return m_Store.m_Data;
			}
		}

		sealed class SceneDataWriter : ISceneDataWriter
		{

			readonly SceneDataStore m_Store;

			public SceneDataWriter (SceneDataStore store)
			{
				m_Store = store;
			}

			public void Write (ISceneData data)
			{
				m_Store.m_Data = data;
			}
		}
	}

	public static class SceneDataStoreExtensions
	{

		/// <summary>
		/// Retrieves <see cref="ISceneData"/> with the specified type. Throws an exception if the type cast fails.
		/// </summary>
		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="InvalidCastException"></exception>
		public static T Read<T> (this ISceneDataReader reader) where T : ISceneData
		{
			if (reader == null)
			{
				throw new ArgumentNullException(nameof(reader));
			}
			return reader.TryRead(out T result) ? result : throw new InvalidCastException($"Data is null or data of type {typeof(T)} is not stored.");
		}

		/// <summary>
		/// Retrieves <see cref="ISceneData"/> with the specified type. Returns default value if type cast fails.
		/// </summary>
		/// <exception cref="ArgumentNullException"></exception>
		public static T ReadOrDefault<T> (this ISceneDataReader reader) where T : ISceneData
		{
			if (reader == null)
			{
				throw new ArgumentNullException(nameof(reader));
			}
			return reader.TryRead(out T result) ? result : default;
		}
	}
}