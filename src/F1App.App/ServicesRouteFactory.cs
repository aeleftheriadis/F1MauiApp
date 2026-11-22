namespace F1App;

/// <summary>
/// Creates a routed page from the container.
/// </summary>
/// <remarks>
/// <c>Routing.RegisterRoute(route, type)</c> would construct the page with <see cref="Activator"/>, which
/// needs a parameterless constructor - every page here takes its view model from the container, so the
/// route has to resolve it the same way the shell's <c>DataTemplate</c> does.
/// </remarks>
internal sealed class ServicesRouteFactory(IServiceProvider services, Type pageType) : RouteFactory
{
    public override Element GetOrCreate() => (Element)services.GetRequiredService(pageType);

    public override Element GetOrCreate(IServiceProvider services) =>
        (Element)services.GetRequiredService(pageType);
}
