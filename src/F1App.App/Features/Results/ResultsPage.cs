using F1App.Theme;

namespace F1App.Features.Results;

/// <summary>Historical timing: pick a session from the weekend, read its published classification.</summary>
public sealed class ResultsPage : ContentPage
{
    private readonly ResultsViewModel _viewModel;
    private readonly Picker _picker;
    private readonly Button _replayButton;
    private bool _loaded;

    public ResultsPage(ResultsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Results";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        _picker = new Picker
        {
            Title = "Session",
            TextColor = Themes.Current.TextPrimary,
            TitleColor = Themes.Current.TextSecondary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            HeightRequest = Tokens.MinTouchTarget,
            HorizontalOptions = LayoutOptions.Fill,
        };
        _picker.SetBinding(Picker.ItemsSourceProperty, nameof(ResultsViewModel.SessionNames));
        _picker.SelectedIndexChanged += async (_, _) =>
        {
            if (_picker.SelectedIndex >= 0)
            {
                await _viewModel.SelectSessionAsync(_picker.SelectedIndex, CancellationToken.None);
            }
        };

        var replay = new Button
        {
            Text = "Replay this session",
            BackgroundColor = Themes.Current.SurfaceMuted,
            TextColor = Themes.Current.TextPrimary,
            HeightRequest = Tokens.MinTouchTarget,
        };

        replay.Clicked += async (_, _) =>
        {
            try
            {
                await _viewModel.ReplaySelectedAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                _viewModel.Status = $"could not switch replay: {exception.GetType().Name}";
            }
        };

        _replayButton = replay;
        Content = BuildContent();

        // The palette swap is the C# equivalent of a dictionary swap: rebuild from the new palette.
        Themes.Changed += (_, _) => Content = BuildContent();

        A11y.WatchStatus(_viewModel, nameof(ResultsViewModel.Status), () => _viewModel.Status);

        ToolbarItems.Add(new ToolbarItem("Reload", null, async () =>
        {
            if (_picker.SelectedIndex >= 0)
            {
                await _viewModel.SelectSessionAsync(_picker.SelectedIndex, CancellationToken.None);
            }
        }));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_loaded)
        {
            _loaded = true;
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var index = await _viewModel.EnsureScheduleAsync(CancellationToken.None);

            if (index >= 0)
            {
                _picker.SelectedIndex = index;
                await _viewModel.SelectSessionAsync(index, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"could not load results: {exception.GetType().Name}";
            F1App.Services.Diagnostics.Write("PAGE", exception, "results page");
        }
    }

    private View BuildContent()
    {
        // The palette swap rebuilds this page; a control created in the constructor still has its old
        // parent, and MAUI refuses to add it twice.
        (_picker.Parent as Layout)?.Remove(_picker);

        var header = new VerticalStackLayout
        {
            Padding = new Thickness(Tokens.Space4, Tokens.Space3),
            Spacing = Tokens.Space2,
            Children =
            {
                Bind(new Label
                {
                    FontSize = Tokens.FontTitle,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Themes.Current.TextPrimary,
                }, Label.TextProperty, nameof(ResultsViewModel.Headline)),
                _picker,
                _replayButton,
                Surfaces.StatusBar(_viewModel, nameof(ResultsViewModel.Status), nameof(ResultsViewModel.IsBusy)),
            },
        };

        var list = new CollectionView
        {
            ItemsSource = _viewModel.Rows,
            ItemTemplate = new DataTemplate(BuildRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = Themes.Current.Background,
            Header = BuildColumnHeader(),
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            BackgroundColor = Themes.Current.Background,
        };

        layout.Add(header, 0, 0);
        layout.Add(list, 0, 1);
        return layout;
    }

    private static View BuildColumnHeader()
    {
        var grid = Grid(32, 54, 82, 58, 58, 58, 72, 68, 30);

        string[] captions = ["P", "DRV", "BEST LAP", "S1", "S2", "S3", "GAP", "TYRE", "PIT"];

        for (var i = 0; i < captions.Length; i++)
        {
            grid.Add(new Label
            {
                Text = captions[i],
                FontSize = Tokens.FontCaption,
                FontFamily = Tokens.MonoFont,
                TextColor = Themes.Current.TextSecondary,
                HorizontalTextAlignment = i == 0 ? TextAlignment.End : TextAlignment.Start,
            }, i);
        }

        return grid;
    }

    private static View BuildRow()
    {
        var grid = Grid(32, 54, 82, 58, 58, 58, 72, 68, 30);
        grid.Padding = new Thickness(Tokens.Space2, Tokens.Space1);
        grid.SetBinding(SemanticProperties.DescriptionProperty, nameof(TimingDisplayRow.AccessibleText));

        var teamRule = new BoxView
        {
            WidthRequest = 3,
            HeightRequest = 20,
            CornerRadius = 1.5f,
            VerticalOptions = LayoutOptions.Center,
        };
        teamRule.SetBinding(BoxView.ColorProperty, nameof(TimingDisplayRow.TeamColor));

        grid.Add(teamRule, 0);
        grid.Add(Cell(nameof(TimingDisplayRow.Position), Themes.Current.TextSecondary, TextAlignment.End), 0);
        grid.Add(Cell(nameof(TimingDisplayRow.Code), Themes.Current.TextPrimary, TextAlignment.Start, bold: true), 1);
        grid.Add(Cell(nameof(TimingDisplayRow.BestLap), Themes.Current.TextPrimary, TextAlignment.Start), 2);
        grid.Add(Cell(nameof(TimingDisplayRow.Sector1), nameof(TimingDisplayRow.Sector1Color), TextAlignment.Start), 3);
        grid.Add(Cell(nameof(TimingDisplayRow.Sector2), nameof(TimingDisplayRow.Sector2Color), TextAlignment.Start), 4);
        grid.Add(Cell(nameof(TimingDisplayRow.Sector3), nameof(TimingDisplayRow.Sector3Color), TextAlignment.Start), 5);
        grid.Add(Cell(nameof(TimingDisplayRow.Gap), Themes.Current.TextSecondary, TextAlignment.Start), 6);
        grid.Add(Cell(nameof(TimingDisplayRow.Tyre), Themes.Current.TextSecondary, TextAlignment.Start), 7);
        grid.Add(Cell(nameof(TimingDisplayRow.Pits), Themes.Current.TextSecondary, TextAlignment.End), 8);

        return grid;
    }

    private static Grid Grid(params double[] widths)
    {
        var grid = new Grid { ColumnSpacing = Tokens.Space1, HeightRequest = 40 };

        foreach (var width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        }

        return grid;
    }

    private static Label Cell(string path, string colourPath, TextAlignment alignment)
    {
        var label = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            HorizontalTextAlignment = alignment,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        label.SetBinding(Label.TextProperty, path);
        label.SetBinding(Label.TextColorProperty, colourPath);
        return label;
    }

    private static Label Cell(string path, Color color, TextAlignment alignment, bool bold = false)
    {
        var label = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = bold ? Tokens.MonoFont : Tokens.MonoFont,
            TextColor = color,
            HorizontalTextAlignment = alignment,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

        label.SetBinding(Label.TextProperty, path);
        return label;
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
