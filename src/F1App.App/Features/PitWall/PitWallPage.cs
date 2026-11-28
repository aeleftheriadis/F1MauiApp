using System.Collections.ObjectModel;
using System.Globalization;
using F1App.Features.News;
using F1App.Services;
using F1App.Theme;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace F1App.Features.PitWall;

/// <summary>
/// Pit-wall HUD: a dense timing tower, a focused telemetry pane, the news feed and the media index.
/// </summary>
/// <remarks>
/// The visual language is a race-engineering wall rather than a consumer app: carbon panels, one red
/// accent, and telemetry accents reserved for data (cyan = focus, purple = personal best, lime = improved,
/// yellow = slower), with monospaced numerals wherever a value can change.
/// The design's radio player is not here on purpose: no free team-radio feed exists, so the media section
/// indexes the open audio/video feeds instead of drawing a play button over silence.
/// </remarks>
public sealed class PitWallPage : ContentPage, IDisposable
{
    private enum Section
    {
        Timing,
        Focus,
        News,
        Media,
    }

    private readonly PitWallViewModel _viewModel;
    private readonly Image _heroArt = new() { Aspect = Aspect.AspectFill, HeightRequest = 96 };
    private readonly F1Images _images;
    private readonly NewsViewModel _news;
    private readonly System.ComponentModel.PropertyChangedEventHandler _onNewsChanged;
    private readonly SessionRunner _runner;

    private readonly ObservableCollection<NewsRow> _visibleNews = [];
    private readonly ObservableCollection<NewsRow> _visibleMedia = [];
    private readonly IDispatcherTimer _tick;

    private SKCanvasView? _focusCanvas;
    private SKFont? _font;
    private SKPaint? _paint;
    private Grid? _body;
    private Section _section = Section.Timing;
    private Button[] _sectionButtons = [];
    private string _newsFilter = "ALL";

    public PitWallPage(PitWallViewModel viewModel, NewsViewModel news, SessionRunner runner, F1Images images)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(news);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(images);

        _viewModel = viewModel;
        _images = images;
        _news = news;
        _runner = runner;
        BindingContext = viewModel;
        Title = "Pit wall";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        Content = BuildContent();

        _tick = Dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(66);
        _tick.Tick += (_, _) =>
        {
            if (_section == Section.Timing || _section == Section.Focus)
            {
                try
                {
                    if (Environment.GetEnvironmentVariable("F1APP_PITWALL_NOTICK") != "1")
                    {
                        _viewModel.Tick();
                    }
                }
                catch (Exception exception)
                {
                    Diagnostics.Write("PAGE", exception, "pitwall tick");
                }
            }

            if (_section == Section.Focus && _focusCanvas?.Handler is not null)
            {
                _focusCanvas.InvalidateSurface();
            }
        };

        // Subscribed between load and unload: the view model is a singleton, so a page that listens from
        // its constructor stays reachable after it is closed and rebuilds lists nobody can see.
        _onNewsChanged = (_, _) =>
        {
            if (_section is Section.News or Section.Media)
            {
                RebuildNews();
            }
        };
        Loaded += (_, _) => _news.PropertyChanged += _onNewsChanged;
        Unloaded += (_, _) => _news.PropertyChanged -= _onNewsChanged;
        Themes.RebuildOnChange(this, BuildContent);
        A11y.WatchStatus(_viewModel, nameof(PitWallViewModel.Status), () => _viewModel.Status);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _tick.Start();
        EnsureSectionLoaded();
    }

    protected override void OnDisappearing()
    {
        _tick.Stop();
        base.OnDisappearing();
    }

    public void Dispose()
    {
        _tick.Stop();

        _focusCanvas?.PaintSurface -= OnFocusPaint;

        _font?.Dispose();
        _paint?.Dispose();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _viewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"pit wall unavailable: {exception.GetType().Name}";
            Diagnostics.Write("PAGE", exception, "pit wall");
        }
    }

    private async Task RefreshNewsAsync(bool force)
    {
        try
        {
            await _news.RefreshAsync(force, max: 40, CancellationToken.None);
            RebuildNews();
        }
        catch (Exception exception)
        {
            Diagnostics.Write("PAGE", exception, "pit wall news");
        }
    }

    /// <summary>Fills the list of the section that is actually on screen.</summary>
    /// <remarks>
    /// Filling both lists crashed the app: adding items to the Media collection view while the News tab
    /// was the visible one killed XAML inside its input machinery (0xc000027b in CoreMessagingXP, at the
    /// same offset as the reported "clicking NEWS closes the app"). Bisected in the smoke walk - the news
    /// list alone is fine, the hidden media list alone crashes. A hidden CollectionView is not something
    /// to be repopulating; switching tabs already refills the list it reveals.
    /// </remarks>
    private void RebuildNews()
    {
        if (_section is not (Section.News or Section.Media))
        {
            return;
        }

        var isNews = _section == Section.News;
        var target = isNews ? _visibleNews : _visibleMedia;

        target.Clear();

        foreach (var row in _news.Items)
        {
            if (isNews)
            {
                if (_newsFilter == "ALL" || string.Equals(row.Source, _newsFilter, StringComparison.Ordinal))
                {
                    target.Add(row);
                }
            }
            else if (string.Equals(row.Kind, "video", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(row.Kind, "podcast", StringComparison.OrdinalIgnoreCase))
            {
                target.Add(row);
            }
        }
    }

    private View BuildContent()
    {
        _body = new Grid { BackgroundColor = Themes.Current.Background };

        _body.Add(BuildTower());
        _body.Add(BuildFocus());
        _body.Add(BuildNews());
        _body.Add(BuildMedia());

        for (var i = 0; i < _body.Children.Count; i++)
        {
            ((View)_body.Children[i]).IsVisible = i == (int)_section;
        }

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

        layout.Add(BuildHero(), 0, 0);
        layout.Add(BuildHeader(), 0, 1);
        layout.Add(BuildFocusBanner(), 0, 2);
        layout.Add(_body, 0, 3);
        layout.Add(BuildBottomBar(), 0, 4);
        return layout;
    }

    /// <summary>
    /// The race's circuit card, above the header.
    /// </summary>
    /// <remarks>
    /// Shown only when the replay carries the race's published name: a slug guessed from a label would be a
    /// 404 dressed as art. Its absence collapses the row, so nothing shifts for a race the catalogue lacks.
    /// </remarks>
    private View BuildHero()
    {
        Surfaces.Detach(_heroArt);

        var strip = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 0 },
            BackgroundColor = Themes.Current.SurfaceMuted,
            IsVisible = false,
            Content = _heroArt,
        };

        var name = _viewModel.MeetingName;

        if (!string.IsNullOrWhiteSpace(name))
        {
            _ = LoadHeroAsync(strip, name);
        }

        return strip;
    }

    private async Task LoadHeroAsync(Border strip, string meetingName)
    {
        try
        {
            var art = await _images.CircuitForMeetingAsync(meetingName, CancellationToken.None).ConfigureAwait(true);

            if (art is null)
            {
                return;
            }

            _heroArt.Source = art;
            strip.IsVisible = true;
        }
        catch (Exception exception)
        {
            Diagnostics.Write("PAGE", exception, "pit wall hero");
        }
    }

    // ---------------------------------------------------------------- header

    private View BuildHeader()
    {
        var accent = new BoxView
        {
            WidthRequest = 4,
            HeightRequest = 28,
            CornerRadius = 2,
            Color = Themes.Current.Live,
            VerticalOptions = LayoutOptions.Center,
        };

        var eyebrow = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Surfaces.Mono("REPLAY", Tokens.FontCaption, Themes.Current.LiveText, bold: true),
                BuildStatusPill(),
            },
        };

        var titles = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                eyebrow,
                Bind(Surfaces.Text("session", Tokens.FontBody, Themes.Current.TextPrimary, bold: true),
                    Label.TextProperty, nameof(PitWallViewModel.Headline)),
                Bind(Surfaces.Mono(string.Empty, 10, Themes.Current.TextSecondary),
                    Label.TextProperty, nameof(PitWallViewModel.Conditions)),
                Bind(Surfaces.Mono(string.Empty, 9, Themes.Current.TeleYellow),
                    Label.TextProperty, nameof(PitWallViewModel.RaceControl)),
            },
        };

        var topRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        topRow.Add(new HorizontalStackLayout { Spacing = Tokens.Space2, Children = { accent, titles } }, 0);

        var stats = new Grid
        {
            ColumnSpacing = Tokens.Space1,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };

        stats.Add(Stat("CARS", nameof(PitWallViewModel.Cars), Themes.Current.TextPrimary), 0);
        stats.Add(Stat("FASTEST", nameof(PitWallViewModel.Fastest), Themes.Current.TeleCyan), 1);
        stats.Add(Stat("DRS", nameof(PitWallViewModel.DrsOpen), Themes.Current.TeleLime), 2);
        stats.Add(Stat("WINDOW", nameof(PitWallViewModel.Window), Themes.Current.TextPrimary), 3);

        return new VerticalStackLayout
        {
            Padding = new Thickness(Tokens.Space4, Tokens.Space3, Tokens.Space4, Tokens.Space2),
            Spacing = Tokens.Space1,
            BackgroundColor = Themes.Current.Surface,
            Children = { topRow, stats },
        };
    }

    private View Stat(string label, string valuePath, Color colour)
    {
        var value = Bind(Surfaces.Mono("—", Tokens.FontCaption, colour, bold: true), Label.TextProperty, valuePath);

        return new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(Tokens.Space2, Tokens.Space1),
            BackgroundColor = Themes.Current.Background,
            Children = { Surfaces.Mono(label, 10, Themes.Current.TextSecondary), value },
        };
    }

    private View BuildStatusPill()
    {
        var pill = new HorizontalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new BoxView
                {
                    WidthRequest = 6,
                    HeightRequest = 6,
                    CornerRadius = 3,
                    Color = Themes.Current.TeleLime,
                    VerticalOptions = LayoutOptions.Center,
                },
                Surfaces.Mono("LIVE", 9, Themes.Current.TeleLime, bold: true),
            },
        };

        SemanticProperties.SetDescription(pill, "Replay running");
        return pill;
    }

    // ---------------------------------------------------------- focus banner

    private View BuildFocusBanner()
    {
        var teamBar = new BoxView
        {
            WidthRequest = 4,
            HeightRequest = 16,
            CornerRadius = 2,
            VerticalOptions = LayoutOptions.Center,
        };
        teamBar.SetBinding(BoxView.ColorProperty, nameof(PitWallViewModel.FocusColor));

        var left = new HorizontalStackLayout
        {
            Spacing = Tokens.Space2,
            Children =
            {
                Surfaces.Mono("FOCUS", 10, Themes.Current.TextSecondary),
                teamBar,
                Bind(Surfaces.Mono("—", Tokens.FontCaption, Themes.Current.TextPrimary, bold: true),
                    Label.TextProperty, nameof(PitWallViewModel.FocusDriver)),
                Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
                    Label.TextProperty, nameof(PitWallViewModel.FocusDetail)),
            },
        };

        var right = new HorizontalStackLayout
        {
            Spacing = Tokens.Space3,
            Children =
            {
                Readout("SPD", nameof(PitWallViewModel.LiveSpeed), Themes.Current.TeleCyan),
                Readout("GEAR", nameof(PitWallViewModel.LiveGear), Themes.Current.TextPrimary),
                Readout("DRS", nameof(PitWallViewModel.LiveDrs), Themes.Current.TeleLime),
            },
        };

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(left, 0);
        row.Add(right, 1);

        return new VerticalStackLayout
        {
            Padding = new Thickness(Tokens.Space4, Tokens.Space1),
            BackgroundColor = Themes.Current.SurfaceRaised,
            Children = { row },
        };
    }

    private View Readout(string label, string valuePath, Color colour)
    {
        var value = Bind(Surfaces.Mono("—", Tokens.FontCaption, colour, bold: true), Label.TextProperty, valuePath);

        return new HorizontalStackLayout
        {
            Spacing = 3,
            Children = { Surfaces.Mono(label, 9, Themes.Current.TextSecondary), value },
        };
    }

    // ----------------------------------------------------------------- tower

    private Grid BuildTower()
    {
        return new Grid
        {
            BackgroundColor = Themes.Current.Background,
            Children =
            {
                new CollectionView
                {
                    ItemsSource = _viewModel.Rows,
                    ItemTemplate = new DataTemplate(BuildRow),
                    SelectionMode = SelectionMode.None,
                    BackgroundColor = Themes.Current.Background,
                    Header = BuildColumnHeader(),
                },
            },
        };
    }

    private View BuildColumnHeader()
    {
        var grid = Columns(header: true);
        string[] captions = ["POS", "DRIVER", "GAP", "INT", "TIRE", "SECT", "KM/H", "G"];

        for (var i = 0; i < captions.Length; i++)
        {
            grid.Add(Surfaces.Mono(captions[i], 9, Themes.Current.TextSecondary), i + 1);
        }

        return grid;
    }

    private static View BuildRow()
    {
        var grid = Columns(header: false);
        grid.SetBinding(SemanticProperties.DescriptionProperty, nameof(PitWallRow.AccessibleText));

        var rail = new BoxView
        {
            WidthRequest = 2,
            HeightRequest = 18,
            CornerRadius = 1,
            Color = Themes.Current.TeleCyan,
            VerticalOptions = LayoutOptions.Center,
        };
        grid.Add(rail, 0);

        var position = Bind(Surfaces.Mono("—", Tokens.FontCaption, Themes.Current.TextPrimary, bold: true),
            Label.TextProperty, nameof(PitWallRow.Position));
        position.HorizontalTextAlignment = TextAlignment.End;
        grid.Add(position, 1);

        var teamBar = new BoxView
        {
            WidthRequest = 3,
            HeightRequest = 16,
            CornerRadius = 1.5f,
            VerticalOptions = LayoutOptions.Center,
        };
        teamBar.SetBinding(BoxView.ColorProperty, nameof(PitWallRow.TeamColor));

        grid.Add(
            new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    teamBar,
                    Bind(Surfaces.Mono("—", Tokens.FontCaption, Themes.Current.TextPrimary, bold: true),
                        Label.TextProperty, nameof(PitWallRow.Driver)),
                },
            },
            2);

        grid.Add(Bind(Surfaces.Mono("—", 11, Themes.Current.TextPrimary), Label.TextProperty, nameof(PitWallRow.Gap)), 3);
        grid.Add(Bind(Surfaces.Mono("—", 11, Themes.Current.TextSecondary), Label.TextProperty, nameof(PitWallRow.Interval)), 4);
        grid.Add(BuildTyre(), 5);
        grid.Add(BuildSectors(), 6);
        grid.Add(Bind(Surfaces.Mono("—", 11, Themes.Current.TeleCyan, bold: true), Label.TextProperty, nameof(PitWallRow.Speed)), 7);
        grid.Add(Bind(Surfaces.Mono("—", 11, Themes.Current.TextSecondary), Label.TextProperty, nameof(PitWallRow.Gear)), 8);

        return grid;
    }

    private static View BuildTyre()
    {
        var glyph = Surfaces.Mono("?", 9, Themes.Current.TextPrimary, bold: true);
        glyph.HorizontalTextAlignment = TextAlignment.Center;
        glyph.SetBinding(Label.TextProperty, nameof(PitWallRow.Tyre));

        var badge = new Border
        {
            WidthRequest = 16,
            HeightRequest = 16,
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Themes.Current.Background,
            VerticalOptions = LayoutOptions.Center,
            Content = glyph,
        };
        badge.SetBinding(Border.StrokeProperty, nameof(PitWallRow.TyreColor));

        return new HorizontalStackLayout
        {
            Spacing = 3,
            Children =
            {
                badge,
                Bind(Surfaces.Mono("—", 9, Themes.Current.TextSecondary), Label.TextProperty, nameof(PitWallRow.TyreAge)),
            },
        };
    }

    private static View BuildSectors()
    {
        var stack = new HorizontalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };

        foreach (var path in new[] { nameof(PitWallRow.Sector1), nameof(PitWallRow.Sector2), nameof(PitWallRow.Sector3) })
        {
            var block = new BoxView
            {
                WidthRequest = 9,
                HeightRequest = 12,
                CornerRadius = 2,
                VerticalOptions = LayoutOptions.Center,
            };
            block.SetBinding(BoxView.ColorProperty, path);
            stack.Children.Add(block);
        }

        return stack;
    }

    private static Grid Columns(bool header)
    {
        var grid = new Grid
        {
            ColumnSpacing = 4,
            Padding = new Thickness(Tokens.Space2, header ? 2 : 0),
            HeightRequest = header ? 24 : 40,
            BackgroundColor = header ? Themes.Current.Surface : Themes.Current.Background,
        };

        // rail | pos | driver | gap | int | tyre | sectors | speed | gear
        foreach (var width in new[] { 4d, 26d, 54d, 58d, 52d, 44d, 44d, 40d, 20d })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        }

        return grid;
    }

    // ------------------------------------------------------------- focus pane

    private Grid BuildFocus()
    {
        _focusCanvas = new SKCanvasView { BackgroundColor = Themes.Current.Background };
        _focusCanvas.PaintSurface += OnFocusPaint;

        var card = Surfaces.Card(
            new Grid { Children = { _focusCanvas } },
            new Thickness(Tokens.Space3, Tokens.Space2));

        var readouts = new Grid
        {
            ColumnSpacing = Tokens.Space2,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };

        readouts.Add(Readout("SPEED", nameof(PitWallViewModel.LiveSpeed), Themes.Current.TeleCyan), 0);
        readouts.Add(Readout("GEAR", nameof(PitWallViewModel.LiveGear), Themes.Current.TextPrimary), 1);
        readouts.Add(Readout("DRS", nameof(PitWallViewModel.LiveDrs), Themes.Current.TeleLime), 2);

        var pane = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Padding = new Thickness(Tokens.Space3, 0),
            BackgroundColor = Themes.Current.Background,
        };

        pane.Add(card, 0, 0);
        pane.Add(readouts, 0, 1);
        return pane;
    }

    private void OnFocusPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        _font ??= new SKFont(SKTypeface.Default, 10);
        _paint ??= new SKPaint { IsAntialias = true };

        var canvas = e.Surface.Canvas;
        canvas.Clear(Themes.Current.Background.ToSKColor());

        var session = _runner.Loaded;

        if (session is null || _viewModel.FocusDriverNumber == 0)
        {
            _paint.Style = SKPaintStyle.Fill;
            _paint.Color = Themes.Current.TextSecondary.ToSKColor();
            canvas.DrawText("no telemetry yet", e.Info.Width / 2f, e.Info.Height / 2f, SKTextAlign.Center, _font, _paint);
            return;
        }

        var samples = session.Samples
            .Where(sample => sample.DriverNumber == _viewModel.FocusDriverNumber)
            .OrderBy(sample => sample.Time)
            .ToArray();

        if (samples.Length < 2)
        {
            return;
        }

        var pad = 10f;
        var width = e.Info.Width - (pad * 2);
        var height = e.Info.Height - (pad * 2.5f);
        var origin = samples[0].Time;
        var span = (samples[^1].Time - origin).TotalSeconds;

        if (span <= 0 || width <= 0 || height <= 0)
        {
            return;
        }

        _paint.Style = SKPaintStyle.Stroke;
        _paint.StrokeWidth = 1;
        _paint.Color = Themes.Current.Border.ToSKColor();

        for (var step = 0; step <= 4; step++)
        {
            var y = pad + (height * step / 4f);
            canvas.DrawLine(pad, y, pad + width, y, _paint);
        }

        using var path = new SKPath();

        for (var i = 0; i < samples.Length; i++)
        {
            var x = pad + (float)((samples[i].Time - origin).TotalSeconds / span * width);
            var y = pad + height - (samples[i].SpeedKph / 360f * height);

            if (i == 0)
            {
                path.MoveTo(x, y);
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        _paint.StrokeWidth = 2;
        _paint.Color = Themes.Current.TeleCyan.ToSKColor();
        canvas.DrawPath(path, _paint);

        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Themes.Current.TextSecondary.ToSKColor();
        canvas.DrawText(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{samples.Min(static s => s.SpeedKph)}–{samples.Max(static s => s.SpeedKph)} kph over {span:F0}s"),
            pad,
            e.Info.Height - 3,
            SKTextAlign.Left,
            _font,
            _paint);
    }

    // ------------------------------------------------------------------ news

    private View BuildNews()
    {
        var filters = new HorizontalStackLayout
        {
            Spacing = Tokens.Space1,
            Padding = new Thickness(Tokens.Space3, Tokens.Space2),
            BackgroundColor = Themes.Current.Surface,
        };

        foreach (var source in new[] { "ALL", "Formula 1", "Autosport", "The Race", "BBC Sport" })
        {
            filters.Children.Add(FilterPill(source));
        }

        var status = new Grid
        {
            Padding = new Thickness(Tokens.Space3, Tokens.Space1),
            BackgroundColor = Themes.Current.Background,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        status.Add(
            new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    new BoxView
                    {
                        WidthRequest = 6,
                        HeightRequest = 6,
                        CornerRadius = 3,
                        Color = Themes.Current.TeleCyan,
                        VerticalOptions = LayoutOptions.Center,
                    },
                    Surfaces.Mono("OPEN RSS AGGREGATOR", 10, Themes.Current.TextSecondary),
                },
            },
            0);

        var sync = new Button
        {
            Text = "SYNC",
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = Tokens.MinTouchTarget,
            BackgroundColor = Themes.Current.Background,
            TextColor = Themes.Current.TeleCyan,
        };
        sync.Clicked += async (_, _) => await RefreshNewsAsync(force: true);
        status.Add(sync, 1);

        var list = new CollectionView
        {
            ItemsSource = _visibleNews,
            ItemTemplate = new DataTemplate(BuildNewsCard),
            SelectionMode = SelectionMode.Single,
            BackgroundColor = Themes.Current.Background,
        };

        list.SelectionChanged += async (_, args) =>
        {
            if (args.CurrentSelection.FirstOrDefault() is NewsRow row)
            {
                list.SelectedItem = null;
                await Launcher.OpenAsync(row.Link);
            }
        };

        var news = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            BackgroundColor = Themes.Current.Background,
        };

        news.Add(filters, 0, 0);
        news.Add(status, 0, 1);
        news.Add(list, 0, 2);
        return news;
    }

    private View FilterPill(string label)
    {
        var selected = string.Equals(_newsFilter, label, StringComparison.Ordinal);

        var pill = new Button
        {
            Text = label,
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 40,
            Padding = new Thickness(Tokens.Space2, 0),
            CornerRadius = 10,
            BackgroundColor = selected ? Themes.Current.Live : Themes.Current.Background,
            TextColor = selected ? Themes.Current.TextPrimary : Themes.Current.TextSecondary,
        };

        pill.Clicked += (_, _) =>
        {
            _newsFilter = label;
            RebuildNews();
            Content = BuildContent();
        };

        return pill;
    }

    private static View BuildNewsCard()
    {
        var title = Bind(Surfaces.Text("—", Tokens.FontBody, Themes.Current.TextPrimary, bold: true),
            Label.TextProperty, nameof(NewsRow.Title));
        title.MaxLines = 3;
        title.LineBreakMode = LineBreakMode.TailTruncation;

        var meta = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        meta.Add(Bind(Surfaces.Mono("—", 10, Themes.Current.TeleCyan), Label.TextProperty, nameof(NewsRow.Source)), 0);

        var age = Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary), Label.TextProperty, nameof(NewsRow.Age));
        age.HorizontalTextAlignment = TextAlignment.End;
        meta.Add(age, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 3,
            MinimumHeightRequest = Tokens.MinTouchTarget,
            Children = { title, meta },
        };

        var card = Surfaces.Card(body, new Thickness(Tokens.Space3, Tokens.Space1));
        card.SetBinding(SemanticProperties.DescriptionProperty, nameof(NewsRow.AccessibleText));
        SemanticProperties.SetHint(card, "Opens the article in your browser");
        return card;
    }

    // ----------------------------------------------------------------- media

    private View BuildMedia()
    {
        var watch = new CollectionView
        {
            ItemsSource = _visibleMedia,
            ItemTemplate = new DataTemplate(BuildMediaCard),
            SelectionMode = SelectionMode.Single,
            BackgroundColor = Themes.Current.Background,
            Header = Surfaces.Mono("WATCH & LISTEN · OPEN FEEDS", 10, Themes.Current.TextSecondary),
        };

        watch.SelectionChanged += async (_, args) =>
        {
            if (args.CurrentSelection.FirstOrDefault() is NewsRow row)
            {
                watch.SelectedItem = null;
                await Launcher.OpenAsync(row.Link);
            }
        };

        var unavailable = Surfaces.Card(
            new VerticalStackLayout
            {
                Spacing = Tokens.Space1,
                Children =
                {
                    Surfaces.Mono("TEAM RADIO · NOT AVAILABLE", 10, Themes.Current.LiveText, bold: true),
                    Surfaces.Text(
                        "No free, keyless source publishes team radio or onboard video. This screen indexes the open " +
                        "audio and video feeds that exist instead of showing a player over silence.",
                        Tokens.FontCaption,
                        Themes.Current.TextSecondary),
                },
            },
            new Thickness(Tokens.Space3, Tokens.Space2));

        var pane = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Padding = new Thickness(Tokens.Space3, Tokens.Space2),
            BackgroundColor = Themes.Current.Background,
        };

        pane.Add(unavailable, 0, 0);
        pane.Add(watch, 0, 1);
        return pane;
    }

    private static View BuildMediaCard()
    {
        var title = Bind(Surfaces.Text("—", Tokens.FontBody, Themes.Current.TextPrimary, bold: true),
            Label.TextProperty, nameof(NewsRow.Title));
        title.MaxLines = 2;
        title.LineBreakMode = LineBreakMode.TailTruncation;

        var kind = Bind(Surfaces.Mono("—", 10, Themes.Current.TeleLime, bold: true),
            Label.TextProperty, nameof(NewsRow.Kind));

        var source = Bind(Surfaces.Mono("—", 10, Themes.Current.TextSecondary),
            Label.TextProperty, nameof(NewsRow.Source));

        var body = new VerticalStackLayout
        {
            Spacing = 3,
            MinimumHeightRequest = Tokens.MinTouchTarget,
            Children = { title, new HorizontalStackLayout { Spacing = Tokens.Space2, Children = { kind, source } } },
        };

        var card = Surfaces.Card(body, new Thickness(Tokens.Space3, Tokens.Space1));
        card.SetBinding(SemanticProperties.DescriptionProperty, nameof(NewsRow.AccessibleText));
        SemanticProperties.SetHint(card, "Opens the feed item in your browser");
        return card;
    }

    // ------------------------------------------------------------ bottom bar

    private View BuildBottomBar()
    {
        var labels = new[] { "TIMING", "FOCUS", "NEWS", "MEDIA" };
        _sectionButtons = new Button[labels.Length];

        var bar = new Grid
        {
            ColumnSpacing = Tokens.Space1,
            Padding = new Thickness(Tokens.Space2, Tokens.Space1),
            BackgroundColor = Themes.Current.Surface,
        };

        for (var i = 0; i < labels.Length; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var index = i;
            var button = new Button
            {
                Text = labels[i],
                // Stable handle for the Ailoha CLI and for any UI test: text is localisable, an id is not.
                AutomationId = $"PitWallSection{i}",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                HeightRequest = Tokens.MinTouchTarget,
                CornerRadius = 12,
                BackgroundColor = (Section)index == _section ? Themes.Current.Live : Themes.Current.Background,
                TextColor = (Section)index == _section ? Themes.Current.TextPrimary : Themes.Current.TextSecondary,
            };

            button.Clicked += (_, _) => ShowSection((Section)index);
            _sectionButtons[i] = button;
            bar.Add(button, i);
        }

        return bar;
    }

    /// <summary>
    /// Starts the work the current section needs, and only that.
    /// </summary>
    /// <remarks>
    /// Loading the classification and ten RSS feeds from a screen the user is about to leave was both
    /// wasteful and the cause of a native crash during navigation: two async pipelines touched views that
    /// were already detached. Section-scoped loading keeps each pipeline tied to a visible screen.
    /// </remarks>
    private void EnsureSectionLoaded()
    {

        switch (_section)
        {
            case Section.Timing:
            case Section.Focus:
                if (!_viewModel.IsLoaded)
                {
                    _ = LoadAsync();
                }

                break;

            case Section.News:
            case Section.Media:
                if (_news.Items.Count == 0)
                {
                    _ = RefreshNewsAsync(force: false);
                }
                else
                {
                    RebuildNews();
                }

                break;
        }
    }

    /// <summary>Smoke-only: presses a section tab, the one interaction the walk cannot reach.</summary>
    internal void SmokeSelectSection(int index) => ShowSection((Section)index);

    /// <summary>Smoke-only: the section tab itself, so a real pointer click can be aimed at it.</summary>
    internal Button SmokeSectionButton(int index) => _sectionButtons[index];

    private void ShowSection(Section section)
    {
        _section = section;

        if (_body is not null)
        {
            for (var i = 0; i < _body.Children.Count; i++)
            {
                ((View)_body.Children[i]).IsVisible = i == (int)section;
            }
        }

        for (var i = 0; i < _sectionButtons.Length; i++)
        {
            var selected = i == (int)section;
            _sectionButtons[i].BackgroundColor = selected ? Themes.Current.Live : Themes.Current.Background;
            _sectionButtons[i].TextColor = selected ? Themes.Current.TextPrimary : Themes.Current.TextSecondary;
        }

        if (section == Section.Focus && _focusCanvas?.Handler is not null)
        {
            _focusCanvas.InvalidateSurface();
        }

        EnsureSectionLoaded();
    }

    private static T Bind<T>(T target, BindableProperty property, string path)
        where T : BindableObject
    {
        target.SetBinding(property, path);
        return target;
    }
}
