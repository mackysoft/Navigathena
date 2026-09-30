namespace MackySoft.Navigathena
{
    /// <summary>Describes the old and required final appearance for one finite animation.</summary>
    public sealed record ScreenAnimation (ScreenAnimationKind Kind, ScreenAnimationState From, ScreenAnimationState To);
}
