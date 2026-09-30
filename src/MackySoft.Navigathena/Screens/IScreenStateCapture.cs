namespace MackySoft.Navigathena
{
    /// <summary>Optionally captures immutable data used when a released screen is reconstructed.</summary>
    public interface IScreenStateCapture
    {
        object? CaptureState ();
    }
}
