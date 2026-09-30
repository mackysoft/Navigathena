namespace MackySoft.Navigathena
{
    internal interface IScreenCallScope
    {
        ScreenCall<TResult> Connect<TResult> ();
    }
}
