using F1App.Theme;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace F1App.Features.Compare;

/// <summary>Two cars' speed traces over the replay window, drawn on a Skia surface.</summary>
/// <remarks>
/// Speed against elapsed time, not against distance: distance is not in the payload, and inventing it by
/// integrating GPS would be a different (and wrong) claim. Time is what the samples actually carry.
/// </remarks>
public sealed class ComparePage : ContentPage, IDisposable
{
    private readonly CompareViewModel _viewModel;
    private readonly SKCanvasView _canvas;
    private readonly Picker _left;
    private readonly Picker _right;
    private readonly SKFont _font = new(SKTypeface.Default, 11);
    private readonly SKPaint _paint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly IDispatcherTimer _timer;
    private bool _disposed;

    public ComparePage(CompareViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        BindingContext = viewModel;
        Title = "Compare";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        _left = NewPicker("Left car", nameof(CompareViewModel.Drivers));
        _right = NewPicker("Right car", nameof(CompareViewModel.Drivers));

        _left.SelectedIndexChanged += (_, _) =>
        {
            _viewModel.LeftIndex = _left.SelectedIndex;
            _viewModel.Build();
            _canvas?.InvalidateSurface();
        };

        _right.SelectedIndexChanged += (_, _) =>
        {
            _viewModel.RightIndex = _right.SelectedIndex;
            _viewModel.Build();
            _canvas?.InvalidateSurface();
        };

        _canvas = new SKCanvasView { BackgroundColor = Themes.Current.Background };
        _canvas.PaintSurface += OnPaintSurface;

        A11y.WatchStatus(_viewModel, nameof(CompareViewModel.Status), () => _viewModel.Status);
        Content = BuildContent();

        // The palette swap is the C# equivalent of a dictionary swap: rebuild from the new palette.
        Themes.Changed += (_, _) => Content = BuildContent();

        // The replay can be switched from another screen, so poll the runner once a second.
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => SyncDrivers();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _timer.Start();
        SyncDrivers();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();
        _canvas.PaintSurface -= OnPaintSurface;
        _font.Dispose();
        _paint.Dispose();
        _disposed = true;
    }

    private void SyncDrivers()
    {
        if (!_viewModel.RefreshDriverList())
        {
            return;
        }

        if (_viewModel.Drivers.Count > 0)
        {
            _left.SelectedIndex = 0;
            _right.SelectedIndex = Math.Min(1, _viewModel.Drivers.Count - 1);
            _viewModel.LeftIndex = _left.SelectedIndex;
            _viewModel.RightIndex = _right.SelectedIndex;
            _viewModel.Build();
            _canvas?.InvalidateSurface();
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
                Bind(new Label
                {
                    FontSize = Tokens.FontTitle,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Themes.Current.TextPrimary,
                }, Label.TextProperty, nameof(CompareViewModel.Headline)),
                _left,
                _right,
                Bind(new Label
                {
                    FontSize = Tokens.FontCaption,
                    FontFamily = Tokens.MonoFont,
                    TextColor = Themes.Current.TextSecondary,
                }, Label.TextProperty, nameof(CompareViewModel.Status)),
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

    private static Picker NewPicker(string title, string itemsPath)
    {
        var picker = new Picker
        {
            Title = title,
            TextColor = Themes.Current.TextPrimary,
            TitleColor = Themes.Current.TextSecondary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            HeightRequest = Tokens.MinTouchTarget,
        };

        picker.SetBinding(Picker.ItemsSourceProperty, itemsPath);
        return picker;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(Themes.Current.Background.ToSKColor());

        var left = _viewModel.Left;
        var right = _viewModel.Right;

        if (left is null && right is null)
        {
            _paint.Style = SKPaintStyle.Fill;
            _paint.Color = Themes.Current.TextSecondary.ToSKColor();
            canvas.DrawText(
                "no replay loaded",
                e.Info.Width / 2f,
                e.Info.Height / 2f,
                SKTextAlign.Center,
                _font,
                _paint);
            return;
        }

        var pad = 32f;
        var width = e.Info.Width - (pad * 2);
        var height = e.Info.Height - (pad * 2);

        var maxSeconds = Math.Max(MaxSeconds(left), MaxSeconds(right));
        const double maxSpeed = 360;

        if (maxSeconds <= 0 || width <= 0 || height <= 0)
        {
            return;
        }

        DrawAxis(canvas, pad, width, height, maxSeconds);

        DrawTrace(canvas, left, Themes.Current.Live.ToSKColor(), pad, width, height, maxSeconds, maxSpeed);
        DrawTrace(canvas, right, Themes.Current.SectorPersonal.ToSKColor(), pad, width, height, maxSeconds, maxSpeed);

        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Themes.Current.TextSecondary.ToSKColor();
        canvas.DrawText(
            $"{left?.Label ?? "—"} (red) vs {right?.Label ?? "—"} (green) · speed kph vs seconds",
            pad,
            e.Info.Height - 10,
            SKTextAlign.Left,
            _font,
            _paint);
    }

    private void DrawAxis(SKCanvas canvas, float pad, float width, float height, double maxSeconds)
    {
        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1;
        _paint.Color = Themes.Current.Border.ToSKColor();

        for (var step = 0; step <= 4; step++)
        {
            var y = pad + (height * step / 4f);
            canvas.DrawLine(pad, y, pad + width, y, _paint);
        }

        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Themes.Current.TextSecondary.ToSKColor();
        canvas.DrawText("0", 4, pad + 12, SKTextAlign.Left, _font, _paint);
        canvas.DrawText(
            $"{maxSeconds:F0}s",
            pad + width - 30,
            pad + height + 14,
            SKTextAlign.Left,
            _font,
            _paint);
    }

    private void DrawTrace(
        SKCanvas canvas,
        SpeedTrace? trace,
        SKColor colour,
        float pad,
        float width,
        float height,
        double maxSeconds,
        double maxSpeed)
    {
        if (trace is null || trace.SecondsFromStart.Length < 2)
        {
            return;
        }

        using var path = new SKPath();

        for (var i = 0; i < trace.SecondsFromStart.Length; i++)
        {
            var x = pad + (float)(trace.SecondsFromStart[i] / maxSeconds * width);
            var y = pad + height - (float)(trace.SpeedKph[i] / maxSpeed * height);

            if (i == 0)
            {
                path.MoveTo(x, y);
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 2;
        _paint.Color = colour;
        canvas.DrawPath(path, _paint);
    }

    private static double MaxSeconds(SpeedTrace? trace) =>
        trace is null || trace.SecondsFromStart.Length == 0 ? 0 : trace.SecondsFromStart[^1];

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
