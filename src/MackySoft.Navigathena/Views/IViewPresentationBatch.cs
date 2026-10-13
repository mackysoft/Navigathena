using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Applies related native views after the current event dispatch has settled.</summary>
    /// <remarks>Revoke affected input before cleanup callbacks. Do not deliver new operations to an intermediate state. On failure, keep the affected input closed.</remarks>
    public interface IViewPresentationBatch
    {
        /// <summary> Completes the permission update and its native input cleanup before returning. </summary>
        ValueTask ApplyAsync (ViewPresentationChangeSet changes);
    }
}
