using F1App.Theme;

namespace F1App.Features.Facts;

/// <summary>Every world champion, newest first.</summary>
public sealed class FactsPage : ContentPage
{
    private readonly FactsViewModel _viewModel;
    private bool _loaded;

    public FactsPage(FactsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Facts";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        Content = BuildContent();

        A11y.WatchStatus(_viewModel, nameof(FactsViewModel.Status), () => _viewModel.Status);

        ToolbarItems.Add(new ToolbarItem("Refresh", null, async () => await LoadAsync(force: true)));
        Themes.Changed += (_, _) => Content = BuildContent();
    }

    private View BuildContent()
    {
        var list = new CollectionView
        {
            ItemsSource = _viewModel.Champions,
            ItemTemplate = new DataTemplate(BuildRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = Themes.Current.Background,
            Header = BuildColumnHeader(),
        };

        var status = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextSecondary,
            Padding = new Thickness(Tokens.Space4, Tokens.Space2),
        };
        status.SetBinding(Label.TextProperty, nameof(FactsViewModel.Status));

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            BackgroundColor = Themes.Current.Background,
        };

        layout.Add(list, 0, 0);
        layout.Add(status, 0, 1);

        return layout;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _ = LoadAsync(force: false);
    }

    private async Task LoadAsync(bool force)
    {
        try
        {
            await _viewModel.LoadAsync(force, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"could not load facts: {exception.GetType().Name}";
            F1App.Services.Diagnostics.Write("PAGE", exception, "facts page");
        }
    }

    private static View BuildColumnHeader() => new Label
    {
        Text = "WORLD CHAMPIONS",
        FontSize = Tokens.FontCaption,
        FontFamily = Tokens.MonoFont,
        TextColor = Themes.Current.TextSecondary,
        Padding = new Thickness(Tokens.Space4, Tokens.Space2),
    };

    private static View BuildRow()
    {
        var season = new Label
        {
            FontSize = Tokens.FontTitle,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextSecondary,
            VerticalTextAlignment = TextAlignment.Center,
            WidthRequest = 64,
        };
        season.SetBinding(Label.TextProperty, nameof(ChampionRow.Season));

        var driver = new Label
        {
            FontSize = Tokens.FontBody,
            FontAttributes = FontAttributes.Bold,
            TextColor = Themes.Current.TextPrimary,
        };
        driver.SetBinding(Label.TextProperty, nameof(ChampionRow.Driver));

        var team = new Label
        {
            FontSize = Tokens.FontCaption,
            TextColor = Themes.Current.TextSecondary,
        };
        team.SetBinding(Label.TextProperty, nameof(ChampionRow.Team));

        var numbers = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextSecondary,
            HorizontalTextAlignment = TextAlignment.End,
        };
        numbers.SetBinding(Label.TextProperty, nameof(ChampionRow.Numbers));

        var text = new VerticalStackLayout { Spacing = 0, Children = { driver, team } };

        var row = new Grid
        {
            ColumnSpacing = Tokens.Space2,
            Padding = new Thickness(Tokens.Space1, Tokens.Space1),
            ColumnDefinitions =
            {
                new ColumnDefinition(64),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        row.SetBinding(SemanticProperties.DescriptionProperty, nameof(ChampionRow.AccessibleText));
        row.Add(season, 0);
        row.Add(text, 1);
        row.Add(numbers, 2);

        return Surfaces.Card(row, new Thickness(Tokens.Space3, Tokens.Space1));
    }
}
