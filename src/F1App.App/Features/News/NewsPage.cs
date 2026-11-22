using F1App.Theme;

namespace F1App.Features.News;

/// <summary>Headlines from the public feeds, tap to read at the source.</summary>
public sealed class NewsPage : ContentPage
{
    private readonly NewsViewModel _viewModel;
    private CollectionView? _list;
    private bool _loaded;

    public NewsPage(NewsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "News";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        Content = BuildContent();
        A11y.WatchStatus(viewModel, nameof(NewsViewModel.Status), () => viewModel.Status);

        ToolbarItems.Add(new ToolbarItem("Refresh", null, async () => await RefreshAsync(force: true)));
        Themes.Changed += (_, _) => Content = BuildContent();
    }

    private View BuildContent()
    {
        _list = new CollectionView
        {
            ItemsSource = _viewModel.Items,
            ItemTemplate = new DataTemplate(BuildRow),
            SelectionMode = SelectionMode.Single,
            BackgroundColor = Themes.Current.Background,
        };

        _list!.SelectionChanged += async (_, args) =>
        {
            if (args.CurrentSelection.FirstOrDefault() is NewsRow row)
            {
                _list!.SelectedItem = null;

                // Opens in the platform browser: the publisher keeps the page view and the copyright.
                await Launcher.OpenAsync(row.Link);
            }
        };

        var status = Surfaces.StatusBar(_viewModel, nameof(NewsViewModel.Status), nameof(NewsViewModel.IsBusy));

        // Pull-to-refresh is the gesture every platform's list has; the toolbar item stays for keyboard users.
        var refresh = new RefreshView { Content = _list! };
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(NewsViewModel.IsBusy), mode: BindingMode.OneWay);
        refresh.Command = new Command(async () => await RefreshAsync(force: false));

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            BackgroundColor = Themes.Current.Background,
        };

        layout.Add(refresh, 0, 0);
        layout.Add(status, 0, 1);

        return layout;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_loaded)
        {
            _loaded = true;
            _ = RefreshAsync(force: false);
        }
    }

    private async Task RefreshAsync(bool force)
    {
        try
        {
            await _viewModel.RefreshAsync(force, max: 60, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"refresh failed: {exception.GetType().Name}";
            F1App.Services.Diagnostics.Write("PAGE", exception, "news page");
        }
    }

    private static View BuildRow()
    {
        var title = new Label
        {
            FontSize = Tokens.FontBody,
            FontAttributes = FontAttributes.Bold,
            TextColor = Themes.Current.TextPrimary,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 3,
        };
        title.SetBinding(Label.TextProperty, nameof(NewsRow.Title));

        var source = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextSecondary,
        };
        source.SetBinding(Label.TextProperty, nameof(NewsRow.Source));

        var age = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextSecondary,
            HorizontalTextAlignment = TextAlignment.End,
        };
        age.SetBinding(Label.TextProperty, nameof(NewsRow.Age));

        var meta = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        meta.Add(source, 0);
        meta.Add(age, 1);

        var body = new VerticalStackLayout
        {
            Spacing = Tokens.Space1,
            MinimumHeightRequest = Tokens.MinTouchTarget,
            Children = { title, meta },
        };

        var card = Surfaces.Card(body, new Thickness(Tokens.Space3, Tokens.Space1));
        card.SetBinding(SemanticProperties.DescriptionProperty, nameof(NewsRow.AccessibleText));
        SemanticProperties.SetHint(card, "Opens the article in your browser");
        return card;
    }
}
