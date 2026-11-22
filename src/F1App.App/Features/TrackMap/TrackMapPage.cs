using F1App.Domain;
using F1App.Telemetry;
using F1App.Theme;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace F1App.Features.TrackMap;

/// <summary>Circuit atlas: every circuit of the season, drawn from its own recorded GPS lap.</summary>
/// <remarks>
/// <para>
/// Static maps, no invented geometry: the shape is the path the cars drove, and the lap length printed
/// under it is measured from those coordinates. Markers are deliberately absent — this screen shows the
/// circuit, not a session, and a dot with no session behind it would be decoration pretending to be data.
/// </para>
/// <para>
/// <see cref="SKCanvasView"/> is CPU-rendered; the path is rebuilt only when a new outline arrives, so a
/// frame costs one path draw plus a caption.
/// </para>
/// </remarks>
public sealed class TrackMapPage : ContentPage, IDisposable
{
    private readonly CircuitMapViewModel _viewModel;
    private readonly TelemetrySnapshotStore _store;
    private readonly IDispatcherTimer _markerTimer;
    private readonly TelemetryFrame[] _frameBuffer = new TelemetryFrame[TelemetrySnapshotStore.MaxDriverNumber];
    private readonly SKFont _markerFont = new(SKTypeface.Default, 9);
    private readonly SKCanvasView _canvas;
    private readonly Picker _picker;
    private readonly SKFont _captionFont = new(SKTypeface.Default, 12);
    private readonly SKPaint _strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _textPaint = new() { IsAntialias = true };
    private SKPath? _centreline;
    private TrackOutline? _outlineSource;
    private bool _loaded;
    private bool _disposed;

    public TrackMapPage(CircuitMapViewModel viewModel, TelemetrySnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(store);

        _viewModel = viewModel;
        _store = store;
        BindingContext = viewModel;
        Title = "Track";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        _canvas = new SKCanvasView { BackgroundColor = Themes.Current.Background };
        _canvas.PaintSurface += OnPaintSurface;

        _picker = new Picker
        {
            Title = "Circuit",
            TextColor = Themes.Current.TextPrimary,
            TitleColor = Themes.Current.TextSecondary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            HeightRequest = Tokens.MinTouchTarget,
        };
        _picker.SetBinding(Picker.ItemsSourceProperty, nameof(CircuitMapViewModel.Circuits));
        _picker.SelectedIndexChanged += async (_, _) =>
        {
            if (_picker.SelectedIndex >= 0)
            {
                await _viewModel.SelectAsync(_picker.SelectedIndex, CancellationToken.None);
            }
        };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        A11y.WatchStatus(_viewModel, nameof(CircuitMapViewModel.Status), () => _viewModel.Status);
        Content = BuildContent();

        // The palette swap is the C# equivalent of a dictionary swap: rebuild from the new palette.
        Themes.Changed += (_, _) => Content = BuildContent();

        // Markers only exist while the map on screen is the session being replayed.
        _markerTimer = Dispatcher.CreateTimer();
        _markerTimer.Interval = TimeSpan.FromMilliseconds(33);
        _markerTimer.Tick += (_, _) =>
        {
            if (_viewModel.IsReplayCircuit)
            {
                _canvas.InvalidateSurface();
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _markerTimer.Start();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _ = LoadAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _canvas.PaintSurface -= OnPaintSurface;
        _markerTimer.Stop();
        _markerFont.Dispose();
        _centreline?.Dispose();
        _captionFont.Dispose();
        _strokePaint.Dispose();
        _textPaint.Dispose();
        _disposed = true;
    }

    private async Task LoadAsync()
    {
        try
        {
            var index = await _viewModel.LoadSeasonAsync(CancellationToken.None);

            if (index >= 0)
            {
                _picker.SelectedIndex = index;
                await _viewModel.SelectAsync(index, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"could not load circuits: {exception.GetType().Name}";
            F1App.Services.Diagnostics.Write("PAGE", exception, "track page");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CircuitMapViewModel.Outline))
        {
            _canvas.InvalidateSurface();
        }
    }

    private View BuildContent()
    {
        var header = new VerticalStackLayout
        {
            Padding = new Thickness(Tokens.Space4, Tokens.Space3),
            Spacing = Tokens.Space2,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = Tokens.Space2,
                    Children =
                    {
                        Bind(new Label
                        {
                            FontSize = Tokens.FontTitle,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Themes.Current.TextPrimary,
                            VerticalTextAlignment = TextAlignment.Center,
                        }, Label.TextProperty, nameof(CircuitMapViewModel.Headline)),
                        ProviderImage(nameof(CircuitMapViewModel.CountryFlagUrl), 22, 15),
                        ProviderImage(nameof(CircuitMapViewModel.TrackIconUrl), 44, 33),
                    },
                },
                _picker,
                Bind(new Label
                {
                    FontSize = Tokens.FontCaption,
                    FontFamily = Tokens.MonoFont,
                    TextColor = Themes.Current.TextSecondary,
                }, Label.TextProperty, nameof(CircuitMapViewModel.Facts)),
                Bind(new Label
                {
                    FontSize = Tokens.FontCaption,
                    FontFamily = Tokens.MonoFont,
                    TextColor = Themes.Current.TextSecondary,
                }, Label.TextProperty, nameof(CircuitMapViewModel.Winners)),
                Bind(new Label
                {
                    FontSize = 9,
                    FontFamily = Tokens.MonoFont,
                    TextColor = Themes.Current.TextSecondary,
                }, Label.TextProperty, nameof(CircuitMapViewModel.ImageCredit)),
                CircuitPhoto(),
                Surfaces.StatusBar(_viewModel, nameof(CircuitMapViewModel.Status), nameof(CircuitMapViewModel.IsBusy)),
            },
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
        layout.Add(_canvas, 0, 1);
        return layout;
    }

    /// <summary>A provider-published image, hidden when the field is empty rather than drawn as a blank box.</summary>
    private static View ProviderImage(string path, double width, double height)
    {
        var image = new Image
        {
            Aspect = Aspect.AspectFit,
            WidthRequest = width,
            HeightRequest = height,
            VerticalOptions = LayoutOptions.Center,
        };
        image.SetBinding(Image.SourceProperty, path);
        image.SetBinding(
            IsVisibleProperty,
            new Binding(path, converter: new NotNullOrEmptyConverter()));

        return image;
    }

    /// <summary>
    /// The circuit's Commons photo with its attribution. Hidden entirely when there is none, because a
    /// grey box reads as a broken image rather than as "no photo published".
    /// </summary>
    private View CircuitPhoto()
    {
        var image = new Image
        {
            Aspect = Aspect.AspectFill,
            HeightRequest = 132,
            BackgroundColor = Themes.Current.SurfaceMuted,
        };
        image.SetBinding(Image.SourceProperty, nameof(CircuitMapViewModel.PhotoUrl));

        var caption = Surfaces.Mono(string.Empty, 9, Themes.Current.TextSecondary);
        caption.SetBinding(Label.TextProperty, nameof(CircuitMapViewModel.PhotoCaption));

        // One binding hides the whole block: an Image bound to null still takes its height and draws an
        // empty rectangle, which reads as a broken picture.
        var block = new VerticalStackLayout { Spacing = 2, Children = { image, caption } };
        block.SetBinding(
            IsVisibleProperty,
            new Binding(nameof(CircuitMapViewModel.PhotoUrl), converter: new NotNullOrEmptyConverter()));

        return block;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(Themes.Current.Background.ToSKColor());

        var outline = _viewModel.Outline;
        if (outline is null)
        {
            _textPaint.Color = Themes.Current.TextSecondary.ToSKColor();
            canvas.DrawText(
                _viewModel.IsBusy ? "loading circuit…" : "no circuit selected",
                e.Info.Width / 2f,
                e.Info.Height / 2f,
                SKTextAlign.Center,
                _captionFont,
                _textPaint);
            return;
        }

        EnsureCentreline(outline);

        var scale = Math.Min(e.Info.Width, e.Info.Height) * 0.86f;
        var offsetX = (e.Info.Width - scale) / 2f;
        var offsetY = (e.Info.Height - scale) / 2f;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);

        _strokePaint.Color = Themes.Current.TextSecondary.ToSKColor();
        _strokePaint.StrokeWidth = 4f / scale;

        if (_centreline is not null)
        {
            canvas.DrawPath(_centreline, _strokePaint);
        }

        // Start/finish marker, taken from the first recorded sample rather than from a guess.
        canvas.Restore();

        var start = outline.Points[0];
        _textPaint.Color = Themes.Current.Live.ToSKColor();
        canvas.DrawCircle(offsetX + (start.X * scale), offsetY + (start.Y * scale), 5f, _textPaint);

        if (_viewModel.IsReplayCircuit)
        {
            DrawMarkers(canvas, offsetX, offsetY, scale);
        }
    }

    private void DrawMarkers(SKCanvas canvas, float offsetX, float offsetY, float scale)
    {
        var count = _store.ReadInto(_frameBuffer);

        for (var i = 0; i < count; i++)
        {
            ref readonly var frame = ref _frameBuffer[i];
            var x = offsetX + (frame.X * scale);
            var y = offsetY + (frame.Y * scale);

            var identity = _viewModel.LoadedDriver(frame.DriverNumber);

            _textPaint.Color = frame.Braking
                ? Themes.Current.Live.ToSKColor()
                : identity is null ? Themes.Current.TextPrimary.ToSKColor() : ParseColour(identity.TeamColorHex);
            canvas.DrawCircle(x, y, 9f, _textPaint);

            _textPaint.Color = Themes.Current.Background.ToSKColor();
            canvas.DrawCircle(x, y, 7f, _textPaint);

            _textPaint.Color = frame.Drs == DrsState.Active
                ? Themes.Current.SectorPersonal.ToSKColor()
                : Themes.Current.TextPrimary.ToSKColor();

            canvas.DrawText(
                identity?.Code ?? frame.DriverNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                x,
                y + 3f,
                SKTextAlign.Center,
                _markerFont,
                _textPaint);
        }
    }

    private static SKColor ParseColour(string hex)
    {
        try
        {
            return SkiaSharp.SKColor.Parse(hex);
        }
        catch (ArgumentException)
        {
            return Themes.Current.TextPrimary.ToSKColor();
        }
    }

    private void EnsureCentreline(TrackOutline outline)
    {
        if (ReferenceEquals(outline, _outlineSource) || outline.Points.IsEmpty)
        {
            return;
        }

        _centreline?.Dispose();
        var path = new SKPath();

        for (var i = 0; i < outline.Points.Length; i++)
        {
            var point = outline.Points[i];

            if (i == 0)
            {
                path.MoveTo(point.X, point.Y);
            }
            else
            {
                path.LineTo(point.X, point.Y);
            }
        }

        path.Close();
        _centreline = path;
        _outlineSource = outline;
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
