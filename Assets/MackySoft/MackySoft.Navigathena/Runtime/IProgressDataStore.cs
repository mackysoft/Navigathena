using System;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Transitions;

namespace MackySoft.Navigathena
{

	public interface IProgressDataStore
	{
		object GetDataNonGeneric ();
	}

	public interface IProgressDataStore<T> : IProgressDataStore
	{
		IProgressDataStore<T> SetData (T data);
		T GetData ();
	}

	public sealed class ProgressDataStore<T> : IProgressDataStore<T>
	{

		T m_Data;

		public ProgressDataStore () : this(default)
		{
		}

		public ProgressDataStore (T data)
		{
			m_Data = data;
		}

		public IProgressDataStore<T> SetData (T data)
		{
			m_Data = data;
			return this;
		}

		public T GetData ()
		{
			return m_Data;
		}

		public object GetDataNonGeneric ()
		{
			return m_Data;
		}
	}

	public static class ProgressDataStore
	{

		/// <summary>
		/// 
		/// </summary>
		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="InvalidCastException"></exception>
		public static IProgressDataStore<T> SetData<T> (this IProgressDataStore store, T data)
		{
			if (store == null)
			{
				throw new ArgumentNullException(nameof(store));
			}
			return ((IProgressDataStore<T>)store).SetData(data);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <exception cref="ArgumentNullException"></exception>
		public static bool TryGetData<T> (this IProgressDataStore store, out T data)
		{
			if (store == null)
			{
				throw new ArgumentNullException(nameof(store));
			}
			if (store is IProgressDataStore<T> typedStore)
			{
				data = typedStore.GetData();
				return true;
			}
			data = default;
			return false;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="InvalidCastException"></exception>
		public static T GetData<T> (this IProgressDataStore store)
		{
			if (store == null)
			{
				throw new ArgumentNullException(nameof(store));
			}
			return ((IProgressDataStore<T>)store).GetData();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <exception cref="ArgumentNullException"></exception>
		public static T GetDataOrDefault<T> (this IProgressDataStore store)
		{
			if (store == null)
			{
				throw new ArgumentNullException(nameof(store));
			}
			return store is IProgressDataStore<T> typedStore ? typedStore.GetData() : default;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <exception cref="ArgumentNullException"></exception>
		public static IProgress<IProgressDataStore> AsProgress (this ITransitionHandle handle)
		{
			if (handle == null)
			{
				throw new ArgumentNullException(nameof(handle));
			}
			return handle is IProgress<IProgressDataStore> progress ? progress : Progress.Create<IProgressDataStore>(null);
		}

		public static IProgress<TFrom> ConvertFrom<TFrom, TTo> (IProgress<TTo> targetProgress, Func<TFrom, TTo> selector)
		{
			if (targetProgress == null)
			{
				throw new ArgumentNullException(nameof(targetProgress));
			}
			if (selector == null)
			{
				throw new ArgumentNullException(nameof(selector));
			}
			return new ConvertProgress<TFrom, TTo>(targetProgress,selector);
		}

		sealed class ConvertProgress<TFrom, TTo> : IProgress<TFrom>
		{

			readonly IProgress<TTo> m_Progress;
			readonly Func<TFrom, TTo> m_Selector;

			public ConvertProgress (IProgress<TTo> targetProgress, Func<TFrom, TTo> selector)
			{
				m_Progress = targetProgress ?? throw new ArgumentNullException(nameof(targetProgress));
				m_Selector = selector ?? throw new ArgumentNullException(nameof(selector));
			}

			public void Report (TFrom value)
			{
				TTo dataStore = m_Selector(value);
				m_Progress.Report(dataStore);
			}
		}
	}
}