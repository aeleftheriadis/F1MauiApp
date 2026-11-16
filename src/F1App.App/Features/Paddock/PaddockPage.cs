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

    public PaddockPage(PaddockViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Paddock";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        _portrait = new Image
        {
            Aspect = Aspect.AspectFill,
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

        Content = new Label { Text = "paddock" };

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

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _viewModel.Refresh();
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

    private View BuildContent()
    {
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
                    new Border
                    {
                        StrokeThickness = 1,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                        BackgroundColor = Themes.Current.SurfaceMuted,
                        Margin = new Thickness(0, Tokens.Space1, 0, 0),
                        Content = _portrait,
                    },
                    _noPhoto,
                    Bind(Surfaces.Mono(string.Empty, 9, Themes.Current.TextSecondary),
                        Label.TextProperty, nameof(PaddockViewModel.PortraitCaption)),
                },
            },
            new Thickness(Tokens.Space3, Tokens.Space2));

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

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            BackgroundColor = Themes.Current.Background,
        };

        layout.Add(dossier, 0, 0);
        layout.Add(roster, 0, 1);
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
