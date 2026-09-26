using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Returns failures from asynchronous presentation completion or cleanup.</summary>
    public sealed record PresentationCompletion (IReadOnlyList<PresentationFailure> Failures)
    {
        public static PresentationCompletion Succeeded { get; } = new(Array.Empty<PresentationFailure>());
    }

}
