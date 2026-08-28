using DeskNote.Core.Models;

namespace DeskNote.Data;

/// <summary>
/// Flat projection of a <c>notes</c> row.
/// </summary>
/// <remarks>
/// SQLite has no boolean or timestamp type, so booleans arrive as <see cref="long"/> and
/// timestamps as <see cref="string"/>. Those are kept in their storage form here and converted in
/// one place by <see cref="ToNote"/>, rather than relying on implicit conversions that differ
/// between providers.
/// </remarks>
internal sealed class NoteRow
{
    /// <summary>Aliased column list shared by every read query, so mapping cannot drift per call site.</summary>
    public const string Columns = """
        id AS Id, title AS Title, content AS Content, format AS Format,
        color_key AS ColorKey, opacity AS Opacity, always_on_top AS AlwaysOnTop, is_open AS IsOpen,
        x AS X, y AS Y, width AS Width, height AS Height, monitor_key AS MonitorKey,
        size_preset AS SizePreset, notebook_id AS NotebookId,
        created_at AS CreatedAt, updated_at AS UpdatedAt, deleted_at AS DeletedAt, rev AS Rev
        """;

    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long Format { get; set; }
    public string ColorKey { get; set; } = string.Empty;
    public double Opacity { get; set; }
    public long AlwaysOnTop { get; set; }
    public long IsOpen { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string MonitorKey { get; set; } = string.Empty;
    public long SizePreset { get; set; }
    public string? NotebookId { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
    public long Rev { get; set; }

    public Note ToNote() => new()
    {
        Id = Guid.Parse(Id),
        Title = Title,
        Content = Content,
        Format = (NoteFormat)Format,
        // Normalize rather than cast: a color written by a newer version must not crash restore.
        ColorKey = NoteColors.Normalize(ColorKey),
        Opacity = Opacity,
        AlwaysOnTop = AlwaysOnTop != 0,
        IsOpen = IsOpen != 0,
        Geometry = new NoteGeometry(X, Y, Width, Height, MonitorKey, (NoteSizePreset)SizePreset),
        NotebookId = NotebookId is null ? null : Guid.Parse(NotebookId),
        CreatedAt = SqliteTime.FromDb(CreatedAt),
        UpdatedAt = SqliteTime.FromDb(UpdatedAt),
        DeletedAt = SqliteTime.FromDbNullable(DeletedAt),
        Rev = Rev,
    };
}
