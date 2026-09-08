using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DeskNote.App.Services;

/// <summary>
/// Localized UI text.
/// </summary>
/// <remarks>
/// <para>
/// Backed by the <c>Strings/{locale}/Resources.resw</c> files compiled into the app's PRI. This
/// works in an unpackaged app: the resource manager reads the PRI sitting next to the executable.
/// </para>
/// <para>
/// The manual override goes through an explicit <see cref="ResourceContext"/> qualifier rather
/// than <c>ApplicationLanguages.PrimaryLanguageOverride</c>, which is a packaged-app concept. That
/// keeps "follow Windows" and "use this language regardless" as the same lookup with a different
/// qualifier, so neither path can drift from the other.
/// </para>
/// </remarks>
public static class Strings
{
    private static readonly ResourceManager Manager = new();
    private static readonly ResourceMap Map = Manager.MainResourceMap.GetSubtree("Resources");
    private static readonly Lock Gate = new();

    private static ResourceContext _context = Manager.CreateResourceContext();

    /// <summary>The locale in force, or null when following Windows.</summary>
    public static string? OverrideLocale { get; private set; }

    /// <summary>Supported UI languages, in the order a settings screen should list them.</summary>
    public static IReadOnlyList<string> SupportedLocales { get; } = ["ko-KR", "en-US"];

    /// <summary>
    /// Chooses the UI language. Pass null, or anything unsupported, to follow the Windows setting.
    /// </summary>
    public static void UseLocale(string? locale)
    {
        lock (Gate)
        {
            var context = Manager.CreateResourceContext();

            if (!string.IsNullOrWhiteSpace(locale)
                && SupportedLocales.Contains(locale, StringComparer.OrdinalIgnoreCase))
            {
                context.QualifierValues["Language"] = locale;
                OverrideLocale = locale;
            }
            else
            {
                OverrideLocale = null;
            }

            _context = context;
        }
    }

    /// <summary>
    /// The string for <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// A missing key returns the key itself rather than throwing or returning empty: a wrong label
    /// that names the thing it should have shown is debuggable, whereas a blank button is not, and
    /// neither should take a note window down.
    /// </remarks>
    public static string Get(string key)
    {
        try
        {
            ResourceContext context;
            lock (Gate)
            {
                context = _context;
            }

            return Map.GetValue(key, context).ValueAsString;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write($"Missing UI string '{key}'", ex);
            return key;
        }
    }

    /// <summary>
    /// The language actually in force, resolved rather than reported.
    /// </summary>
    /// <remarks>
    /// <see cref="OverrideLocale"/> is null while the app follows Windows, which is the answer to
    /// a different question. Callers outside the resource system - the pet's chatter corpus, for
    /// one - need to know which language is on screen right now, not whether the user picked it.
    /// </remarks>
    public static string ActiveLocale =>
        OverrideLocale
        ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
                "ko",
                StringComparison.OrdinalIgnoreCase)
            ? "ko-KR"
            : "en-US");

    /// <summary>The string for <paramref name="key"/> with <paramref name="args"/> substituted.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
