using System.Collections.Generic;
namespace MackySoft.Navigathena
{
    public sealed class NavigationTerminationSnapshot
    {
        internal NavigationTerminationSnapshot (long revision, IReadOnlyList<NavigationTerminationRecord> records)
        {
            Revision = revision;
            Records = records;
        }
        public long Revision
        {
            get;
        }
        public IReadOnlyList<NavigationTerminationRecord> Records
        {
            get;
        }
    }
}
