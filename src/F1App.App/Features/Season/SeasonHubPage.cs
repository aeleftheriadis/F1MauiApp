using F1App.Services;
using F1App.Theme;

namespace F1App.Features.Season;

/// <summary>Season hub: pick a year, see the rounds in local time with the next race called out.</summary>
public sealed class SeasonHubPage : ContentPage
{
    private readonly SeasonHubViewModel _viewModel;
    private readonly Picker _year;
    private bool _loaded;
    private readonly WidthSwitch _width;

    public SeasonHubPage(SeasonHubViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        _width = new WidthSwitch(Responsive.IsWideAt(Width), () => Content = BuildContent());
        Title = "Season";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        _year = new Picker
        {
            TextColor = Themes.Current.TextPrimary,
            TitleColor = Themes.Current.TextSecondary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            MinimumHeightRequest = Tokens.MinPickerHeight,
            ItemsSource = (System.Collections.IList)_viewModel.Years,
            SelectedIndex = _viewModel.YearIndex,
        };
        _year.SelectedIndexChanged += async (_, _) =>
        {
            if (_year.SelectedIndex >= 0)
            {
                _viewModel.YearIndex = _year.SelectedIndex;
                await LoadAsync(refresh: false);
            }
        };

        Content = BuildContent();

        Themes.Changed += (_, _) => Content = BuildContent();
        A11y.WatchStatus(_viewModel, nameof(SeasonHubViewModel.Status), () => _viewModel.Status);
    }

    /// <inheritdoc />
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        _width.Observe(width);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _ = LoadAsync(refresh: false);
    }

    private async Task LoadAsync(bool refresh)
    {
        try
        {
            await _viewModel.LoadAsync(refresh, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"calendar unavailable: {exception.GetType().Name}";
            Diagnostics.Write("PAGE", exception, "season hub");
        }
    }

    private View BuildContent()
    {
        // Controls created once in the constructor must leave the old tree before the rebuild.
        Surfaces.Detach(_year);


        var header = new VerticalStackLayout
        {
            Padding = new Thickness(Tokens.Space4, Tokens.Space3, Tokens.Space4, Tokens.Space2),
            Spacing = Tokens.Space1,
            BackgroundColor = Themes.Current.Surface,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = Tokens.Space2,
                    Children =
                    {
                        new BoxView
                        {
                            WidthRequest = 4,
                            HeightRequest = 26,
                            CornerRadius = 2,
                            Color = Themes.Current.Live,
                            VerticalOptions = LayoutOptions.Center,
                        },
                        Bind(Surfaces.Text("season", Tokens.FontBody, Themes.Current.TextPrimary, bold: true),
                            Label.TextProperty, nameof(SeasonHubViewModel.Headline)),
                    },
                },
                Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
                    Label.TextProperty, nameof(SeasonHubViewModel.Subhead)),
                _year,
            },
        };

        var hero = Surfaces.Card(
            new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    Surfaces.Mono("NEXT RACE", 10, Themes.Current.LiveText, bold: true),
                    Bind(Surfaces.Text("—", Tokens.FontSubtitle, Themes.Current.TextPrimary, bold: true),
                        Label.TextProperty, nameof(SeasonHubViewModel.HeroTitle)),
                    Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
                        Label.TextProperty, nameof(SeasonHubViewModel.HeroDetail)),
                    Bind(Surfaces.Mono("—", Tokens.FontCaption, Themes.Current.TeleCyan, bold: true),
                        Label.TextProperty, nameof(SeasonHubViewModel.HeroCountdown)),
                },
            },
            new Thickness(Tokens.Space3, Tokens.Space1));

        var list = new CollectionView
        {
            ItemsSource = _viewModel.Rounds,
            ItemTemplate = new DataTemplate(BuildRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = Themes.Current.Background,
        };

        // Pull-to-refresh re-fetches the calendar; the footer carries the spinner instead of a bare label.
        var refresh = new RefreshView { Content = list };
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(SeasonHubViewModel.IsBusy), mode: BindingMode.OneWay);
        refresh.Command = new Command(async () => await LoadAsync(refresh: true));

        var status = Surfaces.StatusBar(_viewModel, nameof(SeasonHubViewModel.Status), nameof(SeasonHubViewModel.IsBusy));

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

        // Wide: the next-race hero sits beside the rounds instead of pushing them off the first screen.
        // It sizes to its content: a card stretched down an empty column reads as a broken layout.
        hero.VerticalOptions = LayoutOptions.Start;
        layout.Add(header, 0, 0);
        layout.Add(
            Responsive.IsWide
                ? Responsive.Panes(refresh, hero, primaryShare: 0.68, wide: _width.IsWide)
                : StackHeroAbove(hero, refresh),
            0,
            1);
        layout.Add(status, 0, 2);
        return layout;
    }

    /// <summary>Phone: the hero card above the rounds, in one column.</summary>
    private static View StackHeroAbove(View hero, View list)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            BackgroundColor = Themes.Current.Background,
        };

        grid.Add(hero, 0, 0);
        grid.Add(list, 0, 1);
        return grid;
    }

    private View BuildRow()
    {
        var round = Bind(Surfaces.Mono("—", 11, Themes.Current.TextSecondary, bold: true),
            Label.TextProperty, nameof(SeasonRound.Round));

        var name = Bind(Surfaces.Text("—", Tokens.FontBody, Themes.Current.TextPrimary, bold: true),
            Label.TextProperty, nameof(SeasonRound.Name));

        var circuit = Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
            Label.TextProperty, nameof(SeasonRound.Circuit));

        var when = Bind(Surfaces.Mono("—", 10, Themes.Current.TeleCyan),
            Label.TextProperty, nameof(SeasonRound.When));

        var state = Bind(Surfaces.Mono("—", 10, Themes.Current.TeleYellow, bold: true),
            Label.TextProperty, nameof(SeasonRound.State));

        var meta = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        meta.Add(when, 0);
        meta.Add(state, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 1,
            MinimumHeightRequest = Tokens.MinTouchTarget,
            Children = { new HorizontalStackLayout { Spacing = Tokens.Space2, Children = { round, name } }, circuit, meta },
        };

        var card = Surfaces.Row(body, Thickness.Zero);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (sender, _) =>
        {
            if (sender is BindableObject { BindingContext: SeasonRound round })
            {
                try
                {
                    // Switching what every other screen follows is a real action: it gets feedback.
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                    await _viewModel.ReplayAsync(round, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _viewModel.Status = $"replay unavailable: {exception.GetType().Name}";
                    Diagnostics.Write("PAGE", exception, "season round replay");
                }
            }
        };
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
