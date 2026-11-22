using F1App.Services;
using F1App.Theme;

namespace F1App.Features.Races;

/// <summary>One race in full: classification, qualifying and pit stops.</summary>
/// <remarks>
/// Reached by tapping a race in the archive. The three lists are stacked in one scroll rather than tabbed:
/// at a race's size they read as one document, and a tab bar inside a pushed page fights the shell's own.
/// </remarks>
public sealed class RaceDetailPage : ContentPage
{
    private readonly RaceDetailViewModel _viewModel;
    private bool _loaded;

    public RaceDetailPage(RaceDetailViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Race";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        Content = BuildContent();

        A11y.WatchStatus(_viewModel, nameof(RaceDetailViewModel.Status), () => _viewModel.Status);
        Themes.Changed += (_, _) => Content = BuildContent();
    }

    /// <summary>Tells the page which race to show; called by the archive before navigating.</summary>
    public void Select(int season, int round)
    {
        _viewModel.Select(season, round);
        _loaded = false;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _viewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"race unavailable: {exception.GetType().Name}";
            Diagnostics.Write("PAGE", exception, "race detail");
        }
    }

    private View BuildContent()
    {
        var header = new VerticalStackLayout
        {
            Padding = new Thickness(Tokens.Space4, Tokens.Space3, Tokens.Space4, Tokens.Space2),
            Spacing = 2,
            Children =
            {
                Bind(
                    Surfaces.Text("race", Tokens.FontTitle, Themes.Current.TextPrimary, bold: true),
                    Label.TextProperty,
                    nameof(RaceDetailViewModel.Headline)),
                Bind(
                    Surfaces.Mono(string.Empty, 10, Themes.Current.TextSecondary),
                    Label.TextProperty,
                    nameof(RaceDetailViewModel.Subhead)),
            },
        };

        var sections = new VerticalStackLayout
        {
            Spacing = Tokens.Space2,
            Padding = new Thickness(Tokens.Space3, 0, Tokens.Space3, Tokens.Space3),
            Children =
            {
                Section("CLASSIFICATION", _viewModel.Classification),
                Section("QUALIFYING", _viewModel.Qualifying),
                Section("PIT STOPS", _viewModel.PitStops),
            },
        };

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
        layout.Add(
            new ScrollView { BackgroundColor = Themes.Current.Background, Content = sections },
            0,
            1);
        layout.Add(
            Surfaces.StatusBar(_viewModel, nameof(RaceDetailViewModel.Status), nameof(RaceDetailViewModel.IsBusy)),
            0,
            2);

        return layout;
    }

    /// <summary>
    /// A titled section, laid out with a bound stack rather than a CollectionView.
    /// </summary>
    /// <remarks>
    /// A CollectionView inside a ScrollView has to be given a fixed height and then scrolls inside a scroll -
    /// two scrollbars for one document. These sections hold a few dozen rows, so virtualisation buys nothing
    /// and the stack lets the page scroll as a whole.
    /// </remarks>
    private static View Section(string title, System.Collections.ObjectModel.ObservableCollection<RaceDetailRow> rows)
    {
        var list = new VerticalStackLayout { Spacing = 0, BackgroundColor = Themes.Current.Background };
        BindableLayout.SetItemsSource(list, rows);
        BindableLayout.SetItemTemplate(list, new DataTemplate(BuildRow));

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = Tokens.FontCaption,
                    FontFamily = Tokens.MonoFont,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Themes.Current.TextSecondary,
                    Padding = new Thickness(0, Tokens.Space2, 0, Tokens.Space1),
                },
                list,
            },
        };
    }

    private static View BuildRow()
    {
        var lead = Bind(
            Surfaces.Mono("—", Tokens.FontBody, Themes.Current.TextSecondary, bold: true),
            Label.TextProperty,
            nameof(RaceDetailRow.Lead));
        lead.WidthRequest = 40;

        var name = Bind(
            Surfaces.Text("—", Tokens.FontBody, Themes.Current.TextPrimary, bold: true),
            Label.TextProperty,
            nameof(RaceDetailRow.Name));

        var detail = Bind(
            Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
            Label.TextProperty,
            nameof(RaceDetailRow.Detail));

        var row = new Grid
        {
            ColumnSpacing = Tokens.Space2,
            ColumnDefinitions =
            {
                new ColumnDefinition(40),
                new ColumnDefinition(140),
                new ColumnDefinition(GridLength.Star),
            },
        };

        row.SetBinding(SemanticProperties.DescriptionProperty, nameof(RaceDetailRow.AccessibleText));
        row.Add(lead, 0);
        row.Add(name, 1);
        row.Add(detail, 2);

        return Surfaces.Row(row, Thickness.Zero);
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
