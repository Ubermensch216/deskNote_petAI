using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>What should be done with a file the user dropped or pasted onto a note.</summary>
/// <param name="Kind">Copy it into the app's own store, or reference it where it is.</param>
/// <param name="Reason">Why, in a form the UI can show if the user asks.</param>
public readonly record struct AttachmentDecision(AttachmentKind Kind, string Reason);

/// <summary>
/// Decides whether an attachment is copied into the note store or merely linked.
/// </summary>
/// <remarks>
/// Report p5 puts it plainly: 대용량은 복사보다 링크 우선. A pasted screenshot has no other home and
/// must be copied or it is lost the moment the clipboard changes; a 400 MB video already lives
/// somewhere the user chose, and duplicating it into an app-managed folder wastes the space twice
/// and makes the note database something they cannot back up casually.
/// </remarks>
public static class AttachmentPolicy
{
    /// <summary>Above this, a file is referenced rather than copied.</summary>
    public const long EmbedSizeLimitBytes = 10L * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
    };

    public static bool IsImage(string fileName) =>
        ImageExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>Decides for a file the user already has on disk.</summary>
    public static AttachmentDecision ForFile(string fileName, long sizeBytes)
    {
        if (sizeBytes > EmbedSizeLimitBytes)
        {
            return new AttachmentDecision(
                AttachmentKind.Link,
                $"{sizeBytes / (1024 * 1024)} MB — 링크로 연결");
        }

        return IsImage(fileName)
            ? new AttachmentDecision(AttachmentKind.Embedded, "이미지 — 메모에 복사")
            : new AttachmentDecision(AttachmentKind.Link, "파일 — 링크로 연결");
    }

    /// <summary>
    /// Decides for image bytes pasted from the clipboard.
    /// </summary>
    /// <remarks>
    /// Always embedded, whatever the size: clipboard content has no path to link to, so the choice
    /// is copy it or lose it.
    /// </remarks>
    public static AttachmentDecision ForClipboardImage() =>
        new(AttachmentKind.Embedded, "붙여넣은 이미지 — 메모에 복사");

    /// <summary>
    /// The Markdown to insert for an attachment.
    /// </summary>
    /// <remarks>
    /// Images use image syntax so a future rendered view shows them inline; everything else
    /// becomes a plain link, which stays readable as source text either way.
    /// </remarks>
    public static string ToMarkdown(string displayName, string path, bool isImage)
    {
        var target = path.Replace(" ", "%20", StringComparison.Ordinal);
        return isImage ? $"![{displayName}]({target})" : $"[{displayName}]({target})";
    }
}
