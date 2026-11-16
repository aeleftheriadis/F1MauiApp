using F1App.Services;
using F1App.Theme;

namespace F1App.Features.Season;

/// <summary>Season hub: pick a year, see the rounds in local time with the next race called out.</summary>
public sealed class SeasonHubPage : ContentPage
{
    private readonly SeasonHubViewModel _viewModel;
    private readonly Picker _year;
    private bool _loaded;

    public SeasonHubPage(SeasonHubViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Season";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        _year = new Picker
        {
            TextColor = Themes.Current.TextPrimary,
            TitleColor = Themes.Current.TextSecondary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            HeightRequest = Tokens.MinTouchTarget,
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
            Footer = Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
                Label.TextProperty, nameof(SeasonHubViewModel.Status)),
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            BackgroundColor = Themes.Current.Background,
        };

        layout.Add(header, 0, 0);
        layout.Add(hero, 0, 1);
        layout.Add(list, 0, 2);
        return layout;
    }

    private static View BuildRow()
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

        return Surfaces.Card(body, new Thickness(Tokens.Space3, Tokens.Space1));
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
