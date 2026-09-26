using System;

namespace MackySoft.Navigathena
{

    /// <summary>Specifies the history operations that may create a route entry.</summary>
    [Flags]
    public enum RouteEntryOperations
    {
        None = 0,
        Push = 1,
        Replace = 2,
        Reset = 4,
    }

}
