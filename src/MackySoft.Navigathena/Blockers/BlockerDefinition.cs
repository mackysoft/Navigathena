using System;

namespace MackySoft.Navigathena
{
    /// <summary>Identifies one reusable blocker and its construction, independently of routes and regions.</summary>
    /// <remarks>Share this definition to share its instance within a host. Independent simultaneous connections require distinct definitions.</remarks>
    public sealed class BlockerDefinition
    {
        public BlockerDefinition (BlockerFactory create)
        {
            Create = create ?? throw new ArgumentNullException(nameof(create));
        }

        internal BlockerFactory Create
        {
            get;
        }
    }
}
