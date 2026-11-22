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

    /// <summary>
    /// A list row as the platform draws one: full width, no corner radius, no shadow, a hairline
    /// separator underneath and a 44 pt minimum height.
    /// </summary>
    /// <remarks>
    /// Cards are for content that is genuinely a group. A list of cards is a web pattern: it reads as
    /// boxes floating on a background rather than as a list, which is why the same data in a native app
    /// is flat rows on the surface colour.
    /// </remarks>
    public static View Row(View content, Thickness margin)
    {
        ArgumentNullException.ThrowIfNull(content);

        var padded = new Border
        {
            Content = content,
            Margin = margin,
            Padding = new Thickness(Tokens.Space4, Tokens.Space2),
            BackgroundColor = Themes.Current.Background,
            StrokeThickness = 0,
        };

        var separator = new BoxView
        {
            HeightRequest = 1,
            Color = Themes.Current.Border,
            Margin = new Thickness(Tokens.Space4, 0, 0, 0),
        };

        return new VerticalStackLayout
        {
            Spacing = 0,
            MinimumHeightRequest = Tokens.MinTouchTarget,
            Children = { padded, separator },
        };
    }

    /// <summary>
    /// The status row every screen ends with: a spinner while work is running, the sentence either way.
    /// </summary>
    /// <remarks>
    /// A load that shows nothing but text looks frozen. The spinner is the platform's own progress affordance
    /// and it is tied to the view model's <c>IsBusy</c>, so a screen cannot report "loading" without showing it.
    /// </remarks>
    public static View StatusBar(object source, string statusPath, string busyPath)
    {
        ArgumentNullException.ThrowIfNull(source);

        var spinner = new ActivityIndicator
        {
            Color = Themes.Current.TextSecondary,
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
            IsRunning = false,
            BindingContext = source,
        };
        spinner.SetBinding(ActivityIndicator.IsRunningProperty, busyPath);
        spinner.SetBinding(VisualElement.IsVisibleProperty, busyPath);

        var label = Mono(string.Empty, Tokens.FontCaption, Themes.Current.TextSecondary);
        label.SetBinding(Label.TextProperty, statusPath);
        label.VerticalTextAlignment = TextAlignment.Center;

        var row = new HorizontalStackLayout
        {
            Spacing = Tokens.Space2,
            Padding = new Thickness(Tokens.Space4, Tokens.Space2),
            Children = { spinner, label },
            BindingContext = source,
        };

        return row;
    }

    /// <summary>
    /// Detaches controls that outlive a page rebuild.
    /// </summary>
    /// <remarks>
    /// Every page rebuilds its content when the palette changes, but controls created once in the constructor
    /// still belong to the previous tree. Re-adding them corrupts the native visual tree: on WinUI it throws
    /// <c>COMException 0x800F1000</c> from <c>LayoutHandler.SetVirtualView</c>, and on the paths that do not
    /// catch it the process dies with no managed trace. Detach before every rebuild.
    /// </remarks>
    public static void Detach(params View?[] views)
    {
        foreach (var view in views)
        {
            if (view?.Parent is Layout parent)
            {
                parent.Remove(view);
            }
        }
    }

    /// <summary>Prose label in the display family: bold is the heading weight, regular is body.</summary>
    /// <remarks>
    /// One change here re-skins every screen's prose, which is the point of routing type through a single
    /// helper. Numerals keep <see cref="Mono"/> so timing columns still align.
    /// </remarks>
    public static Label Text(string text, double size, Color colour, bool bold = false) => new()
    {
        Text = text,
        FontFamily = bold ? Tokens.DisplayFont : Tokens.BodyFont,
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
