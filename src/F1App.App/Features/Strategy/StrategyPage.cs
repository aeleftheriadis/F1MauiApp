using F1App.Theme;

namespace F1App.Features.Strategy;

/// <summary>Tyre strategy board: one strip per driver, coloured by compound.</summary>
public sealed class StrategyPage : ContentPage
{
    private readonly StrategyViewModel _viewModel;
    private readonly Picker _picker;
    private bool _loaded;

    public StrategyPage(StrategyViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Strategy";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        _picker = new Picker
        {
            Title = "Session",
            TextColor = Themes.Current.TextPrimary,
            TitleColor = Themes.Current.TextSecondary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            HeightRequest = Tokens.MinTouchTarget,
        };
        _picker.SetBinding(Picker.ItemsSourceProperty, nameof(StrategyViewModel.SessionNames));
        _picker.SelectedIndexChanged += async (_, _) =>
        {
            if (_picker.SelectedIndex >= 0)
            {
                await _viewModel.SelectAsync(_picker.SelectedIndex, CancellationToken.None);
            }
        };

        Content = BuildContent();
        A11y.WatchStatus(_viewModel, nameof(StrategyViewModel.Status), () => _viewModel.Status);
        Themes.Changed += (_, _) => Content = BuildContent();
    }

    private View BuildContent()
    {
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
                }, Label.TextProperty, nameof(StrategyViewModel.Headline)),
                _picker,
                Bind(new Label
                {
                    FontSize = Tokens.FontCaption,
                    FontFamily = Tokens.MonoFont,
                    TextColor = Themes.Current.TextSecondary,
                }, Label.TextProperty, nameof(StrategyViewModel.Status)),
            },
        };

        var list = new CollectionView
        {
            ItemsSource = _viewModel.Rows,
            ItemTemplate = new DataTemplate(BuildRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = Themes.Current.Background,
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
            var index = await _viewModel.EnsureScheduleAsync(CancellationToken.None);

            if (index >= 0)
            {
                _picker.SelectedIndex = index;
                await _viewModel.SelectAsync(index, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"could not load strategy: {exception.GetType().Name}";
            F1App.Services.Diagnostics.Write("PAGE", exception, "strategy page");
        }
    }

    private static View BuildRow()
    {
        var code = new Label
        {
            FontSize = Tokens.FontCaption,
            FontAttributes = FontAttributes.Bold,
            TextColor = Themes.Current.TextPrimary,
            WidthRequest = 44,
            VerticalTextAlignment = TextAlignment.Center,
        };
        code.SetBinding(Label.TextProperty, nameof(StrategyRow.Code));

        // One box per stint, width proportional to laps, colour from the compound.
        var strip = new HorizontalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        strip.SetBinding(BindableLayout.ItemsSourceProperty, nameof(StrategyRow.Blocks));
        BindableLayout.SetItemTemplate(strip, new DataTemplate(() =>
        {
            var box = new BoxView { HeightRequest = 14, CornerRadius = 2 };

            box.SetBinding(VisualElement.WidthRequestProperty, nameof(StintBlock.Width));
            box.SetBinding(BoxView.ColorProperty, nameof(StintBlock.Colour));
            // The strip carries information, so each block states its own compound and lap range.
            box.SetBinding(SemanticProperties.DescriptionProperty, nameof(StintBlock.Description));

            return box;
        }));

        var summary = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            TextColor = Themes.Current.TextSecondary,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        summary.SetBinding(Label.TextProperty, nameof(StrategyRow.Summary));

        var row = new Grid
        {
            ColumnSpacing = Tokens.Space2,
            Padding = new Thickness(Tokens.Space3, Tokens.Space2),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(44),
                new ColumnDefinition(GridLength.Star),
            },
        };

        row.SetBinding(SemanticProperties.DescriptionProperty, nameof(StrategyRow.AccessibleText));
        row.Add(code, 0);
        row.SetRowSpan(code, 2);
        row.Add(strip, 1);
        row.Add(summary, 1);
        row.SetRow(summary, 1);

        return row;
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
