namespace F1App.Theme;

/// <summary>Card surfaces, so every screen raises content the same way.</summary>
public static class Surfaces
{
    /// <summary>Wraps content in a raised, rounded card with the platform's own shadow.</summary>
    public static Border Card(View content, Thickness margin)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new Border
        {
            Content = content,
            Margin = margin,
            Padding = new Thickness(Tokens.Space3, Tokens.Space2),
            BackgroundColor = Themes.Current.Surface,
            Stroke = Themes.Current.Border,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = Tokens.RadiusCard,
            },
            Shadow = new Shadow { Opacity = 0f },
        };
    }

    /// <summary>Prose label in the platform's own font at <paramref name="size"/>.</summary>
    public static Label Text(string text, double size, Color colour, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        TextColor = colour,
    };

    /// <summary>Numeric label in the platform's monospaced face, so columns line up.</summary>
    public static Label Mono(string text, double size, Color colour, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = Tokens.MonoFont,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        TextColor = colour,
    };
}
