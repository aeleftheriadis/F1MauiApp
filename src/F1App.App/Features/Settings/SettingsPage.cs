using F1App.Theme;
using F1App.Telemetry.News;

namespace F1App.Features.Settings;

/// <summary>Settings and an honest list of where the data comes from.</summary>
public sealed class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Settings";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;
        Content = BuildContent();

        // The palette swap is the C# equivalent of a dictionary swap: rebuild from the new palette.
        Themes.Changed += (_, _) => Content = BuildContent();
        A11y.WatchStatus(_viewModel, nameof(SettingsViewModel.Observation), () => _viewModel.Observation);
    }

    private View BuildContent()
    {
        var sources = new VerticalStackLayout { Spacing = Tokens.Space1 };

        foreach (var source in NewsSources.All)
        {
            sources.Children.Add(new Label
            {
                Text = $"· {source.Name}",
                FontSize = Tokens.FontCaption,
                FontFamily = Tokens.MonoFont,
                TextColor = Themes.Current.TextSecondary,
            });
        }

        var themePicker = new Picker
        {
            Title = "Theme",
            TextColor = Themes.Current.TextPrimary,
            TitleColor = Themes.Current.TextSecondary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            HeightRequest = Tokens.MinTouchTarget,
            ItemsSource = _viewModel.ThemeModes,
        };
        themePicker.SetBinding(Picker.SelectedIndexProperty, nameof(SettingsViewModel.ThemeIndex), BindingMode.TwoWay);
        SemanticProperties.SetDescription(themePicker, "App theme: follow system, light, or dark");

        var liveToggle = new Switch
        {
            OnColor = Themes.Current.Live,
            ThumbColor = Themes.Current.TextPrimary,
            HorizontalOptions = LayoutOptions.End,
            // A switch is ~31 dp tall on Android; the tappable area has to be the platform minimum.
            HeightRequest = Tokens.MinTouchTarget,
            WidthRequest = Tokens.MinTouchTarget + 8,
        };
        liveToggle.SetBinding(Switch.IsToggledProperty, nameof(SettingsViewModel.LiveFeedEnabled), BindingMode.TwoWay);
        SemanticProperties.SetDescription(liveToggle, "Enable the unofficial Formula 1 live timing feed");

        var testButton = new Button
        {
            Text = "Test live feed",
            BackgroundColor = Themes.Current.SurfaceMuted,
            TextColor = Themes.Current.TextPrimary,
            HeightRequest = Tokens.MinTouchTarget,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0, Tokens.TouchSpacing, 0, 0),
        };
        testButton.SetBinding(IsEnabledProperty, nameof(SettingsViewModel.CanTest));

        testButton.Clicked += async (_, _) => await _viewModel.TestLiveFeedAsync(CancellationToken.None);

        var observation = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextSecondary,
        };
        observation.SetBinding(Label.TextProperty, nameof(SettingsViewModel.Observation));

        return new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(Tokens.Space4),
                Spacing = Tokens.Space3,
                Children =
                {
                    Section("APPEARANCE"),
                    themePicker,
                    Section("LIVE FEED (EXPERIMENTAL)"),
                    new Label
                    {
                        Text = "Formula 1's own timing hub. No key, but undocumented and outside the open-API rule; " +
                               "payload shapes are unverified until a live session, so it does not drive the screens yet.",
                        FontSize = Tokens.FontCaption,
                        TextColor = Themes.Current.TextSecondary,
                    },
                    liveToggle,
                    testButton,
                    observation,
                    Section("NEWS SOURCES"),
                    sources,
                    Section("NOT INCLUDED"),
                    new Label
                    {
                        Text = "Video, onboards, 4K and team radio have no free source. Nothing in this app pretends to have them.",
                        FontSize = Tokens.FontCaption,
                        TextColor = Themes.Current.TextSecondary,
                    },
                },
            },
        };
    }

    private static Label Section(string text) => new()
    {
        Text = text,
        FontSize = Tokens.FontCaption,
        FontFamily = Tokens.MonoFont,
        TextColor = Themes.Current.TextPrimary,
        Margin = new Thickness(0, Tokens.Space2, 0, 0),
    };
}
