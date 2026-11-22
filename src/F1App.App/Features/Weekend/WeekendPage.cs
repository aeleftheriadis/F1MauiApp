using F1App.Theme;

namespace F1App.Features.Weekend;

/// <summary>The weekend hub: what meeting is on, and when the next session starts.</summary>
public sealed class WeekendPage : ContentPage
{
    private readonly WeekendViewModel _viewModel;
    private readonly IDispatcherTimer _clockTimer;
    private bool _loaded;

    public WeekendPage(WeekendViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Weekend";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;
        Content = BuildContent();

        // The palette swap is the C# equivalent of a dictionary swap: rebuild from the new palette.
        Themes.Changed += (_, _) => Content = BuildContent();

        // The phase only changes at session boundaries; a 30 s re-evaluation keeps the countdown honest
        // without touching the network.
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(30);
        _clockTimer.Tick += (_, _) => _viewModel.Recompute();

        A11y.WatchStatus(_viewModel, nameof(WeekendViewModel.Status), () => _viewModel.Status);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _clockTimer.Start();

        if (!_loaded)
        {
            _loaded = true;
            _ = LoadAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _clockTimer.Stop();
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _viewModel.RefreshAsync(force: false, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"could not load the timetable: {exception.GetType().Name}";
            F1App.Services.Diagnostics.Write("PAGE", exception, "weekend page");
        }
    }

    private View BuildContent()
    {
        var header = new VerticalStackLayout
        {
            Padding = new Thickness(Tokens.Space4, Tokens.Space3),
            Spacing = Tokens.Space1,
            Children =
            {
                Bind(new Label
                {
                    FontSize = Tokens.FontCaption,
                    FontFamily = Tokens.MonoFont,
                    TextColor = Themes.Current.TextSecondary,
                    Text = "RACE WEEKEND",
                }, Label.TextProperty, nameof(WeekendViewModel.Headline)),
                Bind(new Label
                {
                    FontSize = Tokens.FontDisplay,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Themes.Current.TextPrimary,
                }, Label.TextProperty, nameof(WeekendViewModel.PhaseLabel)),
                Bind(new Label
                {
                    FontSize = Tokens.FontTitle,
                    FontFamily = Tokens.MonoFont,
                    TextColor = Themes.Current.LiveText,
                }, Label.TextProperty, nameof(WeekendViewModel.Countdown)),
                Surfaces.StatusBar(_viewModel, nameof(WeekendViewModel.Status), nameof(WeekendViewModel.IsBusy)),
            },
        };

        var list = new CollectionView
        {
            ItemsSource = _viewModel.Sessions,
            ItemTemplate = new DataTemplate(BuildRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = Themes.Current.Background,
            Margin = new Thickness(Tokens.Space2, 0),
        };

        var refresh = new Button
        {
            Text = "Refresh timetable",
            BackgroundColor = Themes.Current.SurfaceMuted,
            TextColor = Themes.Current.TextPrimary,
            FontSize = Tokens.FontBody,
            HeightRequest = Tokens.MinTouchTarget,
            Margin = new Thickness(Tokens.Space4, Tokens.Space2),
        };

        refresh.Clicked += async (_, _) => await LoadAsync();

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            BackgroundColor = Themes.Current.Background,
        };

        layout.Add(header, 0, 0);
        layout.Add(list, 0, 1);
        layout.Add(refresh, 0, 2);
        return layout;
    }

    private static View BuildRow()
    {
        var name = new Label
        {
            FontSize = Tokens.FontBody,
            FontAttributes = FontAttributes.Bold,
            TextColor = Themes.Current.TextPrimary,
        };
        name.SetBinding(Label.TextProperty, nameof(SessionRow.Name));

        var when = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextSecondary,
        };
        when.SetBinding(Label.TextProperty, nameof(SessionRow.When));

        // The marker is a word, not just a colour: colour alone is not an indicator.
        var marker = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextPrimary,
            BackgroundColor = Themes.Current.Live,
            Padding = new Thickness(Tokens.Space1, 0),
            HorizontalOptions = LayoutOptions.Start,
        };
        marker.SetBinding(Label.TextProperty, nameof(SessionRow.Marker));

        var body = new VerticalStackLayout
        {
            Spacing = Tokens.Space1,
            Children = { name, when, marker },
        };

        return Surfaces.Card(body, new Thickness(Tokens.Space3, Tokens.Space1));
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
