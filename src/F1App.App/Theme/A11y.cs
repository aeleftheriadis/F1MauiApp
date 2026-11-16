using System.ComponentModel;

namespace F1App.Theme;

/// <summary>Accessibility plumbing used by every screen.</summary>
public static class A11y
{
    /// <summary>
    /// Announces a status line whenever it changes.
    /// </summary>
    /// <remarks>
    /// A status label that only appears on screen is a visual-only error indicator: the platform screen
    /// reader never reads it unless the user navigates there. `SemanticScreenReader.Announce` is the
    /// native equivalent of a live region, so failures ("rate limited", "unavailable") are spoken.
    /// </remarks>
    /// <remarks>
    /// Announcing must never be able to fail the caller. On unpackaged WinUI
    /// <c>SemanticScreenReader.Announce</c> throws <see cref="NullReferenceException"/> - the accessibility
    /// peer is not there - and because the announcement runs inside a property-changed handler, that
    /// exception used to re-enter the caller's catch block and escape as an unobserved task exception.
    /// Windows has no announcement target here, so it is skipped, and the call is guarded either way.
    /// </remarks>
    public static void WatchStatus<T>(T source, string propertyName, Func<string?> read)
        where T : INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(read);

        if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
        {
            return;
        }

        source.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, propertyName, StringComparison.Ordinal))
            {
                return;
            }

            var text = read();

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                SemanticScreenReader.Announce(text);
            }
            catch (Exception exception)
            {
                Services.Diagnostics.Write("A11Y", exception, "status announcement failed");
            }
        };
    }
}
