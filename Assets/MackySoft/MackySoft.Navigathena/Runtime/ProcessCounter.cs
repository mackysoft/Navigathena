using System;

namespace MackySoft.Navigathena
{
	public sealed class ProcessCounter
	{

		int m_ProcessCount;
		int m_Version;

		public bool IsProcessing => m_ProcessCount > 0;
		public int Version => m_Version;

		public ProcessScope Increment () => new ProcessScope(this);

		public readonly struct ProcessScope : IDisposable
		{

			readonly ProcessCounter m_Owner;

			public ProcessScope (ProcessCounter owner)
			{
				m_Owner = owner;
				m_Owner.m_ProcessCount++;
				m_Owner.m_Version++;
			}

			public void Dispose ()
			{
				m_Owner.m_ProcessCount--;
			}
		}
	}
}