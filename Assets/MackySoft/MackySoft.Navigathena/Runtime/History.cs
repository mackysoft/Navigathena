using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{

	public class History<T> : IReadOnlyCollection<T>
	{

		const int kDefauktCapacity = 8;

		readonly Stack<T> m_Stack;

		public int Count => m_Stack.Count;

		public History ()
		{
			m_Stack = new Stack<T>(kDefauktCapacity);
		}

		public History (IEnumerable<T> entries)
		{
			if (entries == null)
			{
				throw new ArgumentNullException(nameof(entries));
			}
			m_Stack = new Stack<T>(entries.Where(x => x != null));
		}

		public void Push (T entry)
		{
			if (entry == null)
			{
				throw new ArgumentNullException(nameof(entry));
			}
			m_Stack.Push(entry);
		}

		public T Pop () => m_Stack.Pop();

		public T Peek () => m_Stack.Peek();

		public bool TryPop (out T result) => m_Stack.TryPop(out result);

		public bool TryPeek (out T result) => m_Stack.TryPeek(out result);

		public void Clear () => m_Stack.Clear();

		public IEnumerator<T> GetEnumerator () => m_Stack.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator () => m_Stack.GetEnumerator();
		
	}
}