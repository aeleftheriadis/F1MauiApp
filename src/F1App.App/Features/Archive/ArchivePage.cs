using System.Globalization;
using F1App.Services;
using F1App.Theme;

namespace F1App.Features.Archive;

/// <summary>The archive: every race since 1950 with its winner, plus the all-time record book.</summary>
/// <remarks>
/// Races and records are two views over one cache, switched by the segmented header rather than stacked:
/// a thousand-row list must not carry a second list under it in the same scroll. The record book is one
/// list with section headers, so drivers and constructors share the scroll they belong to.
/// </remarks>
public sealed class ArchivePage : ContentPage
{
    private readonly ArchiveViewModel _viewModel;
    private readonly IServiceProvider _services;
    private bool _loaded;
    private readonly WidthSwitch _width;

    public ArchivePage(ArchiveViewModel viewModel, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(services);

        _viewModel = viewModel;
        _services = services;
        BindingContext = viewModel;
        _width = new WidthSwitch(Responsive.IsWideAt(Width), () => Content = BuildContent());
        Title = "Archive";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;

        Content = BuildContent();

        A11y.WatchStatus(_viewModel, nameof(ArchiveViewModel.Status), () => _viewModel.Status);
        Themes.Changed += (_, _) => Content = BuildContent();
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
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _viewModel.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.Status = $"archive unavailable: {exception.GetType().Name}";
            Diagnostics.Write("PAGE", exception, "archive page");
        }
    }

    private View BuildContent()
    {
        var search = new Entry
        {
            Placeholder = "Search GP, circuit, country, winner…",
            PlaceholderColor = Themes.Current.TextSecondary,
            TextColor = Themes.Current.TextPrimary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            FontFamily = Tokens.MonoFont,
            FontSize = Tokens.FontCaption,
            HeightRequest = Tokens.MinTouchTarget,
        };
        search.SetBinding(Entry.TextProperty, nameof(ArchiveViewModel.SearchText));

        var decade = new Picker
        {
            TextColor = Themes.Current.TextPrimary,
            TitleColor = Themes.Current.TextSecondary,
            BackgroundColor = Themes.Current.SurfaceMuted,
            MinimumHeightRequest = Tokens.MinPickerHeight,
            MinimumWidthRequest = 96,
            ItemsSource = (System.Collections.IList)_viewModel.Decades,
        };
        decade.SetBinding(Picker.SelectedIndexProperty, nameof(ArchiveViewModel.DecadeIndex));

        var onThisDay = Surfaces.Mono(string.Empty, Tokens.FontCaption, Themes.Current.LiveText, bold: true);
        onThisDay.SetBinding(Label.TextProperty, nameof(ArchiveViewModel.OnThisDay));
        onThisDay.Padding = new Thickness(Tokens.Space4, Tokens.Space1);

        var races = new CollectionView
        {
            ItemsSource = _viewModel.Races,
            Header = onThisDay,
            ItemTemplate = new DataTemplate(BuildRaceRow),
            SelectionMode = SelectionMode.None,
            BackgroundColor = Themes.Current.Background,
            EmptyView = Surfaces.Mono("no races match", Tokens.FontCaption, Themes.Current.TextSecondary),
        };
        races.SetBinding(IsVisibleProperty, nameof(ArchiveViewModel.ShowRaces));

        var records = new CollectionView
        {
            ItemsSource = _viewModel.Records,
            ItemTemplate = new RecordRowTemplate(BuildRecordRow, BuildSectionHeader),
            SelectionMode = SelectionMode.None,
            BackgroundColor = Themes.Current.Background,
        };
        records.SetBinding(
            IsVisibleProperty,
            new Binding(nameof(ArchiveViewModel.ShowRaces), converter: new InverseBoolConverter()));

        // Both lists live in the same cell; the segmented header decides which is on screen.
        var lists = new Grid { BackgroundColor = Themes.Current.Background, Children = { races, records } };

        var refresh = new RefreshView { Content = lists };
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(ArchiveViewModel.IsBusy), mode: BindingMode.OneWay);
        refresh.Command = new Command(async () => await LoadAsync());

        // Wide: races and the record book are both visible, so the segment is not needed. Phone: one at a time.
        if (_width.IsWide)
        {
            // Both panes are on screen at once here, so the RACES/RECORDS toggle does not apply: drop the
            // visibility bindings rather than leaving something that could hide a pane later.
            races.RemoveBinding(IsVisibleProperty);
            records.RemoveBinding(IsVisibleProperty);
            races.IsVisible = true;
            records.IsVisible = true;

            var split = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(0.62, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(0.38, GridUnitType.Star)),
                },
                ColumnSpacing = Tokens.Space3,
                BackgroundColor = Themes.Current.Background,
            };

            var wideLayout = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                },
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                BackgroundColor = Themes.Current.Background,
            };

            wideLayout.Add(search, 0, 0);
            wideLayout.Add(decade, 1, 0);

            // The races pane goes in with its RefreshView, not around it: the list is the refresh wrapper's
            // content, and adding it elsewhere would re-parent it out from under its own pull-to-refresh.
            Surfaces.Detach(records);

            split.Add(refresh, 0);
            split.Add(records, 1);

            wideLayout.Add(split, 0, 1);
            Grid.SetColumnSpan(split, 2);

            var wideStatus = Surfaces.StatusBar(
                _viewModel,
                nameof(ArchiveViewModel.Status),
                nameof(ArchiveViewModel.IsBusy));
            wideLayout.Add(wideStatus, 0, 2);
            Grid.SetColumnSpan(wideStatus, 2);
            return wideLayout;
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
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            BackgroundColor = Themes.Current.Background,
        };

        var segment = Segment();
        layout.Add(segment, 0, 0);
        Grid.SetColumnSpan(segment, 2);

        layout.Add(search, 0, 1);
        layout.Add(decade, 1, 1);
        layout.Add(refresh, 0, 2);
        Grid.SetColumnSpan(refresh, 2);

        var status = Surfaces.StatusBar(_viewModel, nameof(ArchiveViewModel.Status), nameof(ArchiveViewModel.IsBusy));
        layout.Add(status, 0, 3);
        Grid.SetColumnSpan(status, 2);

        return layout;
    }

    /// <summary>The two views: the races vault and the record book.</summary>
    private View Segment()
    {
        var racesButton = SegmentButton("RACES", selected: true);
        var recordsButton = SegmentButton("RECORDS", selected: false);

        racesButton.SetBinding(
            Button.BackgroundColorProperty,
            new Binding(nameof(ArchiveViewModel.ShowRaces), converter: new SegmentSelectedConverter()));
        recordsButton.SetBinding(
            Button.BackgroundColorProperty,
            new Binding(
                nameof(ArchiveViewModel.ShowRaces),
                converter: new SegmentSelectedConverter(),
                converterParameter: "inverse"));

        racesButton.Clicked += (_, _) => _viewModel.ShowRaces = true;
        recordsButton.Clicked += (_, _) => _viewModel.ShowRaces = false;

        return new HorizontalStackLayout
        {
            Spacing = Tokens.Space1,
            Padding = new Thickness(Tokens.Space3, Tokens.Space2),
            Children = { racesButton, recordsButton },
        };
    }

    private static Button SegmentButton(string text, bool selected) => new()
    {
        Text = text,
        FontFamily = Tokens.MonoFont,
        FontSize = Tokens.FontCaption,
        FontAttributes = FontAttributes.Bold,
        TextColor = Themes.Current.TextPrimary,
        BackgroundColor = selected ? Themes.Current.SurfaceRaised : Themes.Current.SurfaceMuted,
        Padding = new Thickness(Tokens.Space3, Tokens.Space1),
    };

    private View BuildRaceRow()
    {
        var season = Surfaces.Mono("—", Tokens.FontCaption, Themes.Current.TextSecondary, bold: true);
        season.SetBinding(Label.TextProperty, nameof(ArchiveRaceRow.Season));
        season.WidthRequest = 44;

        var round = Surfaces.Mono("—", Tokens.FontCaption, Themes.Current.TextSecondary);
        round.SetBinding(Label.TextProperty, nameof(ArchiveRaceRow.Round));
        round.WidthRequest = 32;

        var name = Surfaces.Text("—", Tokens.FontBody, Themes.Current.TextPrimary, bold: true);
        name.SetBinding(Label.TextProperty, nameof(ArchiveRaceRow.RaceName));

        var circuit = Surfaces.Mono("—", 10, Themes.Current.TextSecondary);
        circuit.SetBinding(Label.TextProperty, nameof(ArchiveRaceRow.Circuit));

        var date = Surfaces.Mono("—", 10, Themes.Current.TextSecondary);
        date.SetBinding(Label.TextProperty, nameof(ArchiveRaceRow.Date));

        var winner = Surfaces.Text("—", Tokens.FontCaption, Themes.Current.LiveText, bold: true);
        winner.SetBinding(Label.TextProperty, nameof(ArchiveRaceRow.Winner));

        var detail = Surfaces.Mono("—", 10, Themes.Current.TextSecondary);
        detail.SetBinding(Label.TextProperty, nameof(ArchiveRaceRow.Detail));

        var body = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                new HorizontalStackLayout { Spacing = Tokens.Space2, Children = { season, round, name } },
                circuit,
                new HorizontalStackLayout { Spacing = Tokens.Space2, Children = { date, winner, detail } },
            },
        };

        var card = Surfaces.Row(body, Thickness.Zero);
        card.SetBinding(SemanticProperties.DescriptionProperty, nameof(ArchiveRaceRow.AccessibleText));

        // Tapping a race opens it in full: classification, qualifying and pit stops, fetched on demand.
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (sender, _) =>
        {
            if (sender is not BindableObject { BindingContext: ArchiveRaceRow race })
            {
                return;
            }

            try
            {
                if (_services.GetService(typeof(Races.RaceDetailPage)) is not Races.RaceDetailPage page)
                {
                    return;
                }

                page.Select(race.SeasonNumber, race.RoundNumber);
                await Shell.Current.Navigation.PushAsync(page);
            }
            catch (Exception exception)
            {
                Diagnostics.Write("PAGE", exception, "archive -> race detail");
            }
        };

        card.GestureRecognizers.Add(tap);
        return card;
    }

    private View BuildRecordRow()
    {
        var rank = Surfaces.Mono("—", Tokens.FontBody, Themes.Current.TextSecondary, bold: true);
        rank.SetBinding(Label.TextProperty, nameof(RecordRow.Rank));
        rank.WidthRequest = 36;

        var name = Surfaces.Text("—", Tokens.FontBody, Themes.Current.TextPrimary, bold: true);
        name.SetBinding(Label.TextProperty, nameof(RecordRow.Name));

        var numbers = Surfaces.Mono("—", Tokens.FontCaption, Themes.Current.TextSecondary);
        numbers.SetBinding(Label.TextProperty, nameof(RecordRow.Numbers));

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(36),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = Tokens.Space2,
        };

        row.SetBinding(SemanticProperties.DescriptionProperty, nameof(RecordRow.AccessibleText));
        row.Add(rank, 0);
        row.Add(name, 1);
        row.Add(numbers, 2);

        return Surfaces.Row(row, Thickness.Zero);
    }

    private static View BuildSectionHeader()
    {
        var label = new Label
        {
            FontSize = Tokens.FontCaption,
            FontFamily = Tokens.MonoFont,
            FontAttributes = FontAttributes.Bold,
            TextColor = Themes.Current.TextSecondary,
            Padding = new Thickness(Tokens.Space4, Tokens.Space2, Tokens.Space4, Tokens.Space1),
        };
        label.SetBinding(Label.TextProperty, nameof(RecordRow.Name));
        return label;
    }
}

/// <summary>Record-book rows: section titles get their own flat caption template.</summary>
internal sealed class RecordRowTemplate(Func<View> row, Func<View> section) : DataTemplateSelector
{
    private readonly DataTemplate _row = new(row);
    private readonly DataTemplate _section = new(section);

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        item is RecordRow { IsSection: true } ? _section : _row;
}

/// <summary>Bool to visibility: true shows the races vault, false the record book.</summary>
internal sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Highlights the active segment of the archive header.</summary>
internal sealed class SegmentSelectedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = parameter as string == "inverse" ? value is not true : value is true;
        return selected ? Themes.Current.SurfaceRaised : Themes.Current.SurfaceMuted;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
