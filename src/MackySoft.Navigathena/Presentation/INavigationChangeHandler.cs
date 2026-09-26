namespace MackySoft.Navigathena.Presentation
{
    /// <summary>Optionally updates a retained lifecycle participant when its own participation or child composition changes.</summary>
    public interface INavigationChangeHandler
    {
        void OnNavigationChanged (PresentationChangeContext context);
    }
}
