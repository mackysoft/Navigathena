using System;
using VContainer;

namespace MackySoft.Navigathena.VContainer.Ownership
{
    internal sealed class VContainerScope : IDisposable
    {
        public IObjectResolver Container { get; set; } = null!;
        private bool disposed;
        public void Dispose ()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Container?.Dispose();
        }
    }
}
