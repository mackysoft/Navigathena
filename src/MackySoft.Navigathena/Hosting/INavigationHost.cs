using System;

namespace MackySoft.Navigathena.Hosting
{

    /// <summary>Owns one navigation world and closes requests, resources, and loss input together.</summary>
    public interface INavigationHost : IAsyncDisposable
    {
        INavigationClient Client
        {
            get;
        }
        INavigationStateSource State
        {
            get;
        }
        INavigationRecoveryClient Recovery
        {
            get;
        }
        INavigationTerminationSource Terminations
        {
            get;
        }
        System.Threading.Tasks.ValueTask ShutdownAsync ();
        RegionInstanceId Root
        {
            get;
        }
    }

}
