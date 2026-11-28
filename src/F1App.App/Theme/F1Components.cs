using Microsoft.Maui.Controls.Shapes;

namespace F1App.Theme;

/// <summary>
/// The components that carry F1.com's look: its hero card, its standings row, its team card, and an image
/// that always says where it came from.
/// </summary>
/// <remarks>
/// Built once and reused, so a screen cannot drift from the language and a credit cannot be forgotten: every
/// picture in the app goes through <see cref="CreditedImage"/>, which is the only place that draws one.
/// </remarks>
public static class F1Components
{
    /// <summary>Credit line for artwork served by F1's own CDN.</summary>
    public const string F1MediaCredit = "Formula 1 media, via OpenF1";

    /// <summary>A 16:9 circuit card with the round and date strip over it.</summary>
    public static View HeroCard(string round, string title, string subtitle, ImageSource? image)
    {
        var art = Image(image, title, Aspect.AspectFill, height: 132);

        var caption = new VerticalStackLayout
        {
            Spacing = 1,
            Padding = new Thickness(Tokens.Space3, Tokens.Space2),
            Children =
            {
                Surfaces.Mono(round.ToUpperInvariant(), 10, Themes.Current.LiveText, bold: true),
                Surfaces.Text(title, Tokens.FontTitle, Themes.Current.TextPrimary, bold: true),
                Surfaces.Mono(subtitle, 10, Themes.Current.TextSecondary),
            },
        };

        var stack = new VerticalStackLayout { Spacing = 0, Children = { art, caption } };

        return new Border
        {
            Stroke = Themes.Current.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = (float)Tokens.RadiusCard },
            BackgroundColor = Themes.Current.Surface,
            Margin = new Thickness(0, 0, 0, Tokens.Space2),
            Content = stack,
        };
    }

    /// <summary>
    /// One standings row: cutout, code, name, team logo and a right-aligned value.
    /// </summary>
    /// <param name="code">Three-letter code, shown whether or not there is a cutout.</param>
    /// <param name="name">Driver name as the feed gives it.</param>
    /// <param name="team">Team name, printed small under the driver.</param>
    /// <param name="value">Right-aligned figure or time; pass an empty string when the row has no figure.</param>
    /// <param name="cutout">Full-body cutout, or null to draw the initials tile.</param>
    /// <param name="logo">Team logo, or null to omit it.</param>
    public static View DriverRow(
        string code,
        string name,
        string team,
        string value,
        ImageSource? cutout,
        ImageSource? logo)
    {
        var grid = new Grid
        {
            HeightRequest = 56,
            ColumnSpacing = Tokens.Space2,
            Padding = new Thickness(0, 0, Tokens.Space2, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(46)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(20)),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        grid.Add(cutout is null
            ? InitialsTile(code, height: 44)
            : Image(cutout, $"{name} portrait", Aspect.AspectFit, height: 44), 0);

        grid.Add(Surfaces.Mono(code, 11, Themes.Current.LiveText, bold: true), 1);

        grid.Add(
            new VerticalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Surfaces.Text(name, Tokens.FontBody, Themes.Current.TextPrimary, bold: true),
                    Surfaces.Mono(team, 10, Themes.Current.TextSecondary),
                },
            },
            2);

        if (logo is not null)
        {
            grid.Add(Image(logo, $"{team} logo", Aspect.AspectFit, height: 14), 3);
        }

        grid.Add(Surfaces.Mono(value, 11, Themes.Current.TextPrimary), 4);

        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = (float)Tokens.RadiusCard },
            BackgroundColor = Colors.Transparent,
            Margin = new Thickness(0, 0, 0, 1),
            Content = grid,
        };
    }

    /// <summary>A team's car cutout over its colour, with the logo beside the name.</summary>
    /// <param name="team">Team name, printed as the card's heading.</param>
    /// <param name="accent">Panel colour behind the car.</param>
    /// <param name="car">The car cutout, or null to show only the heading.</param>
    /// <param name="logo">Team logo, or null to omit it.</param>
    /// <param name="showCredit">
    /// False when the screen already prints the credit once for the whole panel - two credits under two
    /// pictures that arrived together reads as a mistake, not as rigour.
    /// </param>
    public static View TeamCard(string team, Color accent, ImageSource? car, ImageSource? logo, bool showCredit = true)
    {
        var header = new HorizontalStackLayout
        {
            Spacing = Tokens.Space2,
            Children =
            {
                Surfaces.Text(team.ToUpperInvariant(), Tokens.FontSubtitle, Themes.Current.TextPrimary, bold: true),
            },
        };

        if (logo is not null)
        {
            header.Children.Add(Image(logo, $"{team} logo", Aspect.AspectFit, height: 16));
        }

        return new Border
        {
            Stroke = Themes.Current.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = (float)Tokens.RadiusCard },
            BackgroundColor = Themes.Current.SurfaceMuted,
            Padding = new Thickness(Tokens.Space3, Tokens.Space2),
            Content = new VerticalStackLayout
            {
                Spacing = Tokens.Space1,
                Children =
                {
                    header,
                    Image(car, $"{team} car", Aspect.AspectFit, height: 96),
                    showCredit
                        ? Surfaces.Mono(F1MediaCredit, 9, Themes.Current.TextSecondary)
                        : new BoxView { HeightRequest = 0 },
                },
            },
        };
    }

    /// <summary>
    /// A picture with its credit line, or the initials tile when there is no picture.
    /// </summary>
    /// <remarks>
    /// The credit is not decoration: it is the condition under which F1's artwork may be shown at all, so it
    /// is a real label that a screen reader reaches, and it is drawn by this method rather than by hand.
    /// </remarks>
    public static View CreditedImage(
        ImageSource? source,
        string description,
        string fallbackCode,
        double height,
        Aspect aspect = Aspect.AspectFit)
    {
        if (source is null)
        {
            return new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    InitialsTile(fallbackCode, height),
                    Surfaces.Mono(F1MediaCredit, 9, Themes.Current.TextSecondary),
                },
            };
        }

        var picture = Image(source, description, aspect, height);
        SemanticProperties.SetDescription(picture, description);

        return new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                picture,
                Surfaces.Mono(F1MediaCredit, 9, Themes.Current.TextSecondary),
            },
        };
    }

    /// <summary>The designed fallback: the code on a muted tile, never an empty frame.</summary>
    public static View InitialsTile(string code, double height)
    {
        var label = Surfaces.Text(
            string.IsNullOrWhiteSpace(code) ? "—" : code.ToUpperInvariant(),
            Tokens.FontSubtitle,
            Themes.Current.TextSecondary,
            bold: true);
        label.HorizontalOptions = LayoutOptions.Center;
        label.VerticalOptions = LayoutOptions.Center;

        return new Border
        {
            HeightRequest = height,
            Stroke = Themes.Current.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = (float)Tokens.RadiusCard },
            BackgroundColor = Themes.Current.SurfaceMuted,
            Content = label,
        };
    }

    private static Image Image(ImageSource? source, string description, Aspect aspect, double height)
    {
        var image = new Image
        {
            Source = source,
            Aspect = aspect,
            HeightRequest = height,
            VerticalOptions = LayoutOptions.Center,
        };

        SemanticProperties.SetDescription(image, description);
        return image;
    }
}
