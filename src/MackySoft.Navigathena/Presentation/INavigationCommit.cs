using System.Collections.Generic;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Provides a one-shot capability to apply one Core-created candidate state.</summary>
    public interface INavigationCommit
    {
        NavigationState Candidate
        {
            get;
        }
        EffectiveComposition CandidateComposition
        {
            get;
        }
        bool IsApplied
        {
            get;
        }
        /// <summary> Atomically saves departing screens' values and applies this candidate once. </summary>
        /// <param name="captures"> Values from current physical screens that this candidate releases while retaining their entries. Use an empty collection when no values are required. </param>
        /// <remarks> Capture must succeed before releasing any source resources. Candidate includes the saved values after this call succeeds. </remarks>
        void Apply (IReadOnlyList<PresentationStateCapture> captures);
    }

}
