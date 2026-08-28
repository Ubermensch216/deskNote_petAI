using System.Globalization;

namespace DeskNote.Data;

/// <summary>
/// Converts timestamps to and from the single textual form used everywhere in the database.
/// </summary>
/// <remarks>
/// Values are always normalized to UTC before formatting. Storing local offsets would break
/// ordering comparisons the moment the user crosses a timezone or DST boundary, and reminders
/// depend on <c>due_at</c> comparing correctly as text.
/// </remarks>
internal static class SqliteTime
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public static string ToDb(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    public static string? ToDb(DateTimeOffset? value) => value is null ? null : ToDb(value.Value);

    public static DateTimeOffset FromDb(string value) =>
        DateTimeOffset.ParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public static DateTimeOffset? FromDbNullable(string? value) =>
        string.IsNullOrEmpty(value) ? null : FromDb(value);
}
