namespace MackySoft.Navigathena
{
    /// <summary>A destination that requires a typed answer recipient.</summary>
    public abstract record Route<TResult> : NavigationRoute;
}
