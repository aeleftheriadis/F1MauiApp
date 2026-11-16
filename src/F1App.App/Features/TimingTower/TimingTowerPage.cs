using F1App.Theme;

namespace F1App.Features.TimingTower;

/// <summary>The live timing tower: 20 virtualized rows, updated in place.</summary>
public sealed class TimingTowerPage : ContentPage
{
    private readonly TimingTowerViewModel _viewModel;
    private readonly IDispatcherTimer _renderTimer;

    public TimingTowerPage(TimingTowerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Timing";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;
        Content = BuildContent();

        // The palette swap is the C# equivalent of a dictionary swap: rebuild from the new palette.
        Themes.Changed += (_, _) => Content = BuildContent();

        _renderTimer = Dispatcher.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(33);
        _renderTimer.Tick += (_, _) => _viewModel.Render();

        // The status line ticks 30 times a second, so it is not announced. The failure flag flips once.
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TimingTowerViewModel.HasFailed) && _viewModel.HasFailed)
            {
                SemanticScreenReader.Announce(_viewModel.StatusLine);
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _renderTimer.Start();
    }

    protected override void OnDisappearing()
    {
        _renderTimer.Stop();
        base.OnDisappearing();
    }

    private View BuildContent()
    {
        var header = new VerticalStackLayout
        {
            Padding = new Thickness(Tokens.Space4, Tokens.Space3),
            Spacing = Tokens.Space1,
            Children =
            {
                BuildSourceBadge(),
                new Label
                {
                    FontSize = Tokens.FontTitle,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Themes.Current.TextPrimary,
                    Text = "Live timing",
                },
            },
        };
        header.Children.Add(Bind(new Label
        {
            FontSize = Tokens.FontBody,
            TextColor = Themes.Current.TextSecondary,
            FontFamily = Tokens.MonoFont,
        }, Label.TextProperty, nameof(TimingTowerViewModel.StatusLine)));

        var columnHeader = BuildColumnHeader();

        var list = new CollectionView
        {
            ItemsSource = _viewModel.Rows,
            ItemTemplate = new DataTemplate(BuildRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = Themes.Current.Background,
            Margin = new Thickness(Tokens.Space2, 0),
        };

        var footer = Bind(new Label
        {
            FontSize = Tokens.FontCaption,
            TextColor = Themes.Current.TextSecondary,
            FontFamily = Tokens.MonoFont,
            Padding = new Thickness(Tokens.Space4, Tokens.Space2),
        }, Label.TextProperty, nameof(TimingTowerViewModel.Diagnostics));

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            BackgroundColor = Themes.Current.Background,
        };

        layout.Add(header, 0, 0);
        layout.Add(columnHeader, 0, 1);
        layout.Add(list, 0, 2);
        layout.Add(footer, 0, 3);

        return layout;
    }

    private View BuildSourceBadge()
    {
        var badge = Bind(new Label
        {
            FontSize = Tokens.FontCaption,
            FontAttributes = FontAttributes.Bold,
            TextColor = Themes.Current.TextPrimary,
            FontFamily = Tokens.MonoFont,
            Padding = new Thickness(Tokens.Space2, Tokens.Space1),
            HorizontalOptions = LayoutOptions.Start,
            BackgroundColor = Themes.Current.SurfaceMuted,
        }, Label.TextProperty, nameof(TimingTowerViewModel.SessionLabel));

        // The badge is a statement of provenance, so it is not decorative: expose it as text to
        // assistive technology rather than relying on the red fill.
        SemanticProperties.SetDescription(badge, "Data source: a recorded session loaded from OpenF1");
        return badge;
    }

    private static View BuildColumnHeader()
    {
        var grid = new Grid
        {
            ColumnSpacing = Tokens.Space2,
            Padding = new Thickness(Tokens.Space3, 0, Tokens.Space3, Tokens.Space1),
            BackgroundColor = Themes.Current.Background,
        };

        foreach (var width in new[] { 30d, 62d, 62d, 58d, 28d, 40d })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var captions = new[] { "#", "DRIVER", "SPEED", "GEAR", "DRS", "THR" };
        for (var i = 0; i < captions.Length; i++)
        {
            grid.Add(new Label
            {
                Text = captions[i],
                FontSize = Tokens.FontCaption,
                TextColor = Themes.Current.TextSecondary,
                FontFamily = Tokens.MonoFont,
                HorizontalTextAlignment = i == 0 ? TextAlignment.End : TextAlignment.Start,
            }, i);
        }

        return grid;
    }

    private static View BuildRow()
    {
        var grid = new Grid
        {
            ColumnSpacing = Tokens.Space2,
            Padding = new Thickness(Tokens.Space3, 0),
            HeightRequest = 40,
        };

        foreach (var width in new[] { 30d, 62d, 62d, 58d, 28d, 40d })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        grid.SetBinding(SemanticProperties.DescriptionProperty, nameof(TimingRow.AccessibleText));

        // Team colour as a 3pt rule on the leading edge: identity, not information.
        var teamRule = new BoxView
        {
            WidthRequest = 3,
            HeightRequest = 22,
            CornerRadius = 1.5f,
            VerticalOptions = LayoutOptions.Center,
        };
        teamRule.SetBinding(BoxView.ColorProperty, nameof(TimingRow.TeamColor));

        var position = Cell(Themes.Current.TextSecondary, TextAlignment.End);
        position.SetBinding(Label.TextProperty, nameof(TimingRow.Position));

        var code = Cell(Themes.Current.TextPrimary, TextAlignment.Start, bold: true);
        code.SetBinding(Label.TextProperty, nameof(TimingRow.Code));

        var speed = Cell(Themes.Current.TextPrimary, TextAlignment.End);
        speed.SetBinding(Label.TextProperty, nameof(TimingRow.Speed));

        var gear = Cell(Themes.Current.TextSecondary, TextAlignment.End);
        gear.SetBinding(Label.TextProperty, nameof(TimingRow.Gear));

        var drs = Cell(Themes.Current.SectorPersonal, TextAlignment.Start, size: Tokens.FontCaption);
        drs.SetBinding(Label.TextProperty, nameof(TimingRow.Drs));

        var throttle = new ProgressBar
        {
            ProgressColor = Themes.Current.SectorPersonal,
            BackgroundColor = Themes.Current.SurfaceMuted,
            HeightRequest = 4,
            VerticalOptions = LayoutOptions.Center,
        };
        throttle.SetBinding(ProgressBar.ProgressProperty, nameof(TimingRow.Throttle));

        var brake = new BoxView
        {
            WidthRequest = 4,
            HeightRequest = 20,
            CornerRadius = 2,
            VerticalOptions = LayoutOptions.Center,
        };
        brake.SetBinding(BoxView.ColorProperty, nameof(TimingRow.BrakeColor));
        SemanticProperties.SetDescription(brake, "Brake indicator");

        grid.Add(teamRule, 0);
        grid.Add(position, 0);
        grid.Add(code, 1);
        grid.Add(speed, 2);
        grid.Add(gear, 3);
        grid.Add(drs, 4);
        grid.Add(throttle, 5);
        grid.Add(brake, 6);

        var rule = new BoxView
        {
            HeightRequest = 1,
            Color = Themes.Current.Border,
            VerticalOptions = LayoutOptions.End,
        };
        Grid.SetColumnSpan(rule, 7);
        grid.Add(rule, 0);

        return grid;
    }

    private static Label Cell(
        Color color,
        TextAlignment alignment,
        bool bold = false,
        double size = Tokens.FontBody) =>
        new()
        {
            FontSize = size,
            FontFamily = bold ? Tokens.MonoFont : Tokens.MonoFont,
            TextColor = color,
            HorizontalTextAlignment = alignment,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
