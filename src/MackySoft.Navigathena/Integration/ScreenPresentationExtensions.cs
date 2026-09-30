using System;

namespace MackySoft.Navigathena.Integration
{
    /// <summary>Integration entry point for platform adapters, separate from ordinary screen factories.</summary>
    public static class ScreenPresentationExtensions
    {
        public static void ConnectPresentation (this ScreenCreationContext creation, ScreenPresentationBinding binding)
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            creation.ConnectPresentation(binding ?? throw new ArgumentNullException(nameof(binding)));
        }
    }
}
