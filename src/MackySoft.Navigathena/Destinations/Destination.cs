namespace MackySoft.Navigathena
{

    /// <summary>Creates immutable navigation destination trees.</summary>
    public static class Destination
    {
        public static NavigationDestinationTree<TRoute> For<TRoute> (TRoute route) where TRoute : Route => new(route);
    }

}
