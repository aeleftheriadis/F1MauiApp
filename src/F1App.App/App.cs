using F1App.Services;
using F1App.Theme;

namespace F1App;

/// <summary>Application entry: composition is the only place that knows the page graph.</summary>
public sealed class App : Application
{
    private readonly IServiceProvider _services;
    private readonly SessionRunner _runner;

    public App(IServiceProvider services, SessionRunner runner)
    {
        _services = services;
        _runner = runner;

        // Application.Current exists from here on, which UserAppTheme needs. Doing it before the shell is
        // built means every page is constructed against the palette the user chose.
        Themes.Initialize();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell(_services, _runner))
        {
            Title = "F1 Telemetry",
        };
}
