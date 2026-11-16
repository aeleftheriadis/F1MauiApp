using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Telemetry.News;

namespace F1App.Features.News;

/// <summary>A headline prepared for display.</summary>
public sealed record NewsRow(string Title, string Source, string Age, string Kind, Uri Link)
{
    /// <summary>One sentence for the screen reader: what it is, who published it, how old it is.</summary>
    public string AccessibleText => $"{Title}. {Source}, {Age} old.";
}

/// <summary>Merged headlines from the public feeds.</summary>
public sealed partial class NewsViewModel : ObservableObject
{
    private readonly NewsService _service;
    private readonly TimeProvider _timeProvider;

    public NewsViewModel(NewsService service, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _service = service;
        _timeProvider = timeProvider;

        Status = "not loaded";
    }

    /// <summary>Headlines, newest first.</summary>
    public ObservableCollection<NewsRow> Items { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Refreshes the merged feed. Headlines only; article bodies stay with the publisher.</summary>
    public async Task RefreshAsync(bool force, int max, CancellationToken cancellationToken)
    {
        IsBusy = true;
        Status = "fetching feeds";

        try
        {
            var snapshot = await _service.GetLatestAsync(max, force, cancellationToken).ConfigureAwait(true);
            var now = _timeProvider.GetUtcNow();

            Items.Clear();

            foreach (var item in snapshot.Items)
            {
                Items.Add(new NewsRow(
                    item.Title,
                    item.Source,
                    Describe(item.Published, now),
                    item.Kind.ToString().ToLowerInvariant(),
                    item.Link));
            }

            var count = Items.Count;

            Status = snapshot.Failures.IsDefaultOrEmpty
                ? $"{count} headlines · {snapshot.FetchedAt.ToLocalTime():HH:mm}"
                : count > 0
                    ? $"{Items.Count} headlines · unavailable: {string.Join(", ", snapshot.Failures)}"
                    : $"every feed failed: {string.Join(", ", snapshot.Failures)}";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Status = $"feeds unreachable: {exception.GetType().Name}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Describe(DateTimeOffset? published, DateTimeOffset now)
    {
        if (published is null)
        {
            return "undated";
        }

        var age = now - published.Value;

        return age.TotalMinutes < 60
            ? $"{(int)Math.Max(1, age.TotalMinutes)}m"
            : age.TotalHours < 24
                ? $"{(int)age.TotalHours}h"
                : $"{(int)age.TotalDays}d";
    }
}
