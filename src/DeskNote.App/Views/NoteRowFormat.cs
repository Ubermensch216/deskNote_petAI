using System.Globalization;
using DeskNote.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DeskNote.App.Views;

/// <summary>A notebook as offered in the Explorer's sidebar.</summary>
/// <param name="Id">The notebook, or null for the "all notebooks" entry.</param>
/// <param name="Label">Text shown in the list.</param>
public sealed record NotebookChoice(Guid? Id, string Label);

/// <summary>A tag as offered in the Explorer's sidebar.</summary>
/// <param name="NormalizedName">Lookup form, or null for the "all tags" entry.</param>
/// <param name="Label">Text shown in the list, including the note count.</param>
public sealed record TagChoice(string? NormalizedName, string Label);

/// <summary>
/// Formatting used by the Explorer's list rows.
/// </summary>
/// <remarks>
/// Written as static functions called from <c>x:Bind</c> rather than as value converters: the
/// compiled binding calls them directly, so there is no converter to register, and the formatting
/// rules stay in one readable place next to the view that uses them.
/// </remarks>
public static class NoteRowFormat
{
    /// <summary>The note's colour, so a row is recognisable as the note sitting on the desktop.</summary>
    public static Brush ColorBrush(string colorKey)
    {
        var isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        return new SolidColorBrush(NotePalette.Resolve(colorKey, isDark).Background);
    }

    /// <summary>
    /// The row's heading: the note's title, or its first line when it has none.
    /// </summary>
    /// <remarks>
    /// Most sticky notes never get a title — they are a thought typed into an empty window — so a
    /// list of "(제목 없음)" rows would be useless. The first line is what the user would call the
    /// note if asked.
    /// </remarks>
    public static string TitleOrFallback(string title, string preview)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var firstLine = preview.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        return string.IsNullOrWhiteSpace(firstLine) ? "빈 메모 / Empty note" : firstLine.Trim();
    }

    /// <summary>The muted line under a row: when it changed, checklist progress, tags, next reminder.</summary>
    public static string Metadata(
        DateTimeOffset updatedAt,
        int checklistDone,
        int checklistTotal,
        IReadOnlyList<string> tags,
        DateTimeOffset? nextReminderAt)
    {
        var parts = new List<string> { RelativeTime(updatedAt) };

        if (checklistTotal > 0)
        {
            parts.Add($"☑ {checklistDone}/{checklistTotal}");
        }

        if (tags.Count > 0)
        {
            parts.Add(string.Join(' ', tags.Select(t => '#' + t)));
        }

        if (nextReminderAt is { } due)
        {
            parts.Add("🔔 " + due.ToLocalTime().ToString("M월 d일 HH:mm", CultureInfo.CurrentCulture));
        }

        return string.Join("  ·  ", parts);
    }

    private static string RelativeTime(DateTimeOffset value)
    {
        var elapsed = DateTimeOffset.UtcNow - value;

        return elapsed switch
        {
            { TotalMinutes: < 1 } => "방금",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes}분 전",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours}시간 전",
            { TotalDays: < 7 } => $"{(int)elapsed.TotalDays}일 전",
            _ => value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }
}
