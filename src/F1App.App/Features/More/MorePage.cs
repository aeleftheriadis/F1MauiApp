using F1App.Theme;

namespace F1App.Features.More;

/// <summary>The phone's overflow tab: the screens that do not earn a bottom tab.</summary>
/// <remarks>
/// A list of rows, each pushing a registered route, is the platform's own shape for this ("More" on iOS,
/// the overflow drawer on Android). The rows come from <see cref="AppShell.Secondary"/>, so adding a screen
/// there adds it here too - no second list to keep in step.
/// </remarks>
public sealed class MorePage : ContentPage
{
    public MorePage()
    {
        Title = "More";
        BackgroundColor = Themes.Current.Background;
        SafeAreaEdges = SafeAreaEdges.Container;
        Content = BuildContent();
        Themes.RebuildOnChange(this, BuildContent);
    }

    private static View BuildContent()
    {
        var list = new VerticalStackLayout { Spacing = 0 };

        foreach (var (route, title, _) in AppShell.Secondary)
        {
            list.Children.Add(Row(title, route));
        }

        return new ScrollView
        {
            BackgroundColor = Themes.Current.Background,
            Content = list,
        };
    }

    /// <summary>
    /// A flat row with a hairline separator and a disclosure chevron - what a platform settings-style list
    /// looks like, instead of a rounded card per entry.
    /// </summary>
    private static View Row(string title, string route)
    {
        var label = Surfaces.Text(title, Tokens.FontBody, Themes.Current.TextPrimary);
        label.VerticalTextAlignment = TextAlignment.Center;

        var chevron = Surfaces.Mono("›", Tokens.FontTitle, Themes.Current.TextSecondary);
        chevron.VerticalTextAlignment = TextAlignment.Center;

        var row = new Grid
        {
            Padding = new Thickness(Tokens.Space4, 0),
            HeightRequest = 48,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            BackgroundColor = Themes.Current.Background,
        };

        row.Add(label, 0);
        row.Add(chevron, 1);
        row.SetBinding(SemanticProperties.DescriptionProperty, static (string t) => t, source: title);

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try
            {
                // Selection feedback where the platform has it; a desktop or an emulator simply ignores it.
                HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                await Shell.Current.GoToAsync(route);
            }
            catch (Exception exception)
            {
                Services.Diagnostics.Write("PAGE", exception, $"more -> {route}");
            }
        };

        row.GestureRecognizers.Add(tap);

        // The separator belongs to the row that follows it, so the list ends flush with the last row.
        var container = new VerticalStackLayout { Spacing = 0, Children = { row, Separator() } };
        return container;
    }

    private static View Separator() => new BoxView
    {
        HeightRequest = 1,
        Color = Themes.Current.Border,
        Margin = new Thickness(Tokens.Space4, 0, 0, 0),
    };
}
