using System;
using System.Threading;
using UnityEngine;

namespace MackySoft.Navigathena.Unity.NativeResources
{
    internal static class UnityThread
    {
        private static int playerThread;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize () => playerThread = Thread.CurrentThread.ManagedThreadId;

        internal static void AssertCurrent ()
        {
            if (playerThread == 0 || playerThread != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException("Unity native resource operations must run on the Unity player thread. Supply its execution context to NavigationHost.Create.");
            }
        }
    }
}
