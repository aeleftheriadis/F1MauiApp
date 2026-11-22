using F1App.Services;
using F1App.Theme;

namespace F1App.Features.Paddock;

/// <summary>Paddock: the session roster, and a dossier with a freely licensed photo.</summary>
public sealed class PaddockPage : ContentPage
{
    private readonly PaddockViewModel _viewModel;
    private readonly Image _portrait;
    private readonly Label _noPhoto;
    private bool _loaded;
    private readonly WidthSwitch _width;

    public PaddockPage(PaddockViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        _width = new WidthSwitch(Responsive.IsWideAt(Width), () => Content = BuildContent());
        Title = "Paddock";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        _portrait = new Image
        {
            // Driver headshots are square and small (93x93 from the provider); filling a 190 pt box cropped
            // them to a chin. Fitting keeps the whole face and does not upscale-crop.
            Aspect = Aspect.AspectFit,
            HeightRequest = 190,
            BackgroundColor = Themes.Current.SurfaceMuted,
            // MAUI caches the downloaded image on disk: one fetch, then offline.
            Source = null,
        };

        _noPhoto = Surfaces.Text(
            "No freely licensed photo found for this driver.",
            Tokens.FontCaption,
            Themes.Current.TextSecondary);
        _noPhoto.IsVisible = false;

        Content = BuildContent();

        Themes.Changed += (_, _) => Content = BuildContent();
        A11y.WatchStatus(_viewModel, nameof(PaddockViewModel.Status), () => _viewModel.Status);
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(PaddockViewModel.PortraitUrl) or nameof(PaddockViewModel.HasNoPortrait))
            {
                ApplyPortrait();
            }
        };
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
        _viewModel.Refresh();

        // The dossier is the pane a wide window shows first; leaving it on an em dash while the duel lines
        // already describe a driver was the state a screenshot caught.
        if (_viewModel.Drivers.Count > 0)
        {
            _ = SelectDriverAsync(0);
        }

        _ = LoadDuelsAsync();
    }

    private async Task LoadDuelsAsync()
    {
        try
        {
            await _viewModel.LoadDuelsAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The dossier is still useful without a duel line; log rather than fail the screen.
            Diagnostics.Write("PAGE", exception, "paddock duels");
        }
    }

    private void ApplyPortrait()
    {
        var url = _viewModel.PortraitUrl;

        if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _portrait.Source = new UriImageSource
            {
                Uri = uri,
                CachingEnabled = true,
                CacheValidity = TimeSpan.FromDays(30),
            };
            _portrait.IsVisible = true;
            _noPhoto.IsVisible = false;
            return;
        }

        _portrait.Source = null;
        _portrait.IsVisible = false;
        _noPhoto.IsVisible = _viewModel.HasNoPortrait;
    }

    /// <summary>Wraps the portrait, and disappears entirely when there is no picture to show.</summary>
    private View PhotoFrame()
    {
        var frame = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            BackgroundColor = Themes.Current.SurfaceMuted,
            Margin = new Thickness(0, Tokens.Space1, 0, 0),
            Content = _portrait,
        };
        frame.SetBinding(
            IsVisibleProperty,
            new Binding(nameof(PaddockViewModel.PortraitUrl), converter: new NotNullOrEmptyConverter()));

        return frame;
    }

    private View BuildContent()
    {
        // Controls created once in the constructor must leave the old tree before the rebuild.
        Surfaces.Detach(_portrait, _noPhoto);


        var dossier = Surfaces.Card(
            new VerticalStackLayout
            {
                Spacing = Tokens.Space1,
                Children =
                {
                    new HorizontalStackLayout
                    {
                        Spacing = Tokens.Space2,
                        Children =
                        {
                            Bind(new BoxView
                            {
                                WidthRequest = 4,
                                HeightRequest = 22,
                                CornerRadius = 2,
                                VerticalOptions = LayoutOptions.Center,
                            }, BoxView.ColorProperty, nameof(PaddockViewModel.DossierColour)),
                            Bind(Surfaces.Text("—", Tokens.FontSubtitle, Themes.Current.TextPrimary, bold: true),
                                Label.TextProperty, nameof(PaddockViewModel.DossierName)),
                        },
                    },
                    Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
                        Label.TextProperty, nameof(PaddockViewModel.DossierDetail)),
                    Bind(Surfaces.Mono(string.Empty, 10, Themes.Current.LiveText),
                        Label.TextProperty, nameof(PaddockViewModel.DuelLine)),
                    Bind(Surfaces.Mono(string.Empty, 10, Themes.Current.TeleCyan),
                        Label.TextProperty, nameof(PaddockViewModel.ChampionshipLine)),
                    PhotoFrame(),
                    _noPhoto,
                    Bind(Surfaces.Mono(string.Empty, 9, Themes.Current.TextSecondary),
                        Label.TextProperty, nameof(PaddockViewModel.PortraitCaption)),
                },
            },
            new Thickness(Tokens.Space3, Tokens.Space2));

        dossier.VerticalOptions = LayoutOptions.Start;

        var roster = new CollectionView
        {
            ItemsSource = _viewModel.Drivers,
            ItemTemplate = new DataTemplate(BuildRow),
            SelectionMode = SelectionMode.Single,
            BackgroundColor = Themes.Current.Background,
        };

        roster.SelectionChanged += async (_, args) =>
        {
            if (args.CurrentSelection.FirstOrDefault() is PaddockDriver)
            {
                var index = _viewModel.Drivers.IndexOf((PaddockDriver)args.CurrentSelection[0]!);
                await SelectDriverAsync(index);
            }
        };

        // Wide: roster beside the dossier. Phone: dossier on top, roster below - as the prototype did.
        var panes = Responsive.Panes(roster, dossier, primaryShare: 0.58, secondaryFirstOnPhone: true, wide: _width.IsWide);

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            BackgroundColor = Themes.Current.Background,
        };

        layout.Add(panes, 0, 0);
        layout.Add(
            Surfaces.StatusBar(_viewModel, nameof(PaddockViewModel.Status), nameof(PaddockViewModel.IsBusy)),
            0,
            1);
        return layout;
    }

    private async Task SelectDriverAsync(int index)
    {
        try
        {
            await _viewModel.SelectAsync(index, CancellationToken.None);
            ApplyPortrait();
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"dossier unavailable: {exception.GetType().Name}";
            Diagnostics.Write("PAGE", exception, "paddock");
        }
    }

    private static View BuildRow()
    {
        var teamBar = new BoxView
        {
            WidthRequest = 3,
            HeightRequest = 22,
            CornerRadius = 1.5f,
            VerticalOptions = LayoutOptions.Center,
        };
        teamBar.SetBinding(BoxView.ColorProperty, nameof(PaddockDriver.Colour));

        var name = Bind(Surfaces.Text("—", Tokens.FontBody, Themes.Current.TextPrimary, bold: true),
            Label.TextProperty, nameof(PaddockDriver.Name));

        var team = Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
            Label.TextProperty, nameof(PaddockDriver.Team));

        var label = Bind(Surfaces.Mono("—", 10, Themes.Current.TeleCyan, bold: true),
            Label.TextProperty, nameof(PaddockDriver.Label));

        var body = new Grid
        {
            ColumnSpacing = Tokens.Space2,
            MinimumHeightRequest = Tokens.MinTouchTarget,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };

        body.Add(teamBar, 0);
        body.SetRowSpan(teamBar, 2);
        body.Add(name, 1);
        body.Add(team, 1);
        body.SetRow(team, 1);
        body.Add(label, 2);
        body.SetRowSpan(label, 2);

        return Surfaces.Card(body, new Thickness(Tokens.Space3, Tokens.Space1));
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
