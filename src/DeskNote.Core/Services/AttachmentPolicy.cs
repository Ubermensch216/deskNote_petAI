using System.Text.RegularExpressions;
using DeskNote.Core.Models;

namespace DeskNote.Core.Services;

/// <summary>What should be done with a file the user dropped or pasted onto a note.</summary>
/// <param name="Kind">Copy it into the app's own store, or reference it where it is.</param>
public readonly record struct AttachmentDecision(AttachmentKind Kind);

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
            return new AttachmentDecision(AttachmentKind.Link);
        }

        return new AttachmentDecision(
            IsImage(fileName) ? AttachmentKind.Embedded : AttachmentKind.Link);
    }

    /// <summary>
    /// Decides for image bytes pasted from the clipboard.
    /// </summary>
    /// <remarks>
    /// Always embedded, whatever the size: clipboard content has no path to link to, so the choice
    /// is copy it or lose it.
    /// </remarks>
    public static AttachmentDecision ForClipboardImage() => new(AttachmentKind.Embedded);

    /// <summary>
    /// The Markdown to insert for an attachment.
    /// </summary>
    /// <remarks>
    /// Images use image syntax so a future rendered view shows them inline; everything else
    /// becomes a plain link, which stays readable as source text either way.
    /// </remarks>
    public static string ToMarkdown(string displayName, string path, bool isImage)
    {
        var target = EscapeTarget(path);
        return isImage ? $"![{displayName}]({target})" : $"[{displayName}]({target})";
    }

    /// <summary>The link target this policy writes for a path.</summary>
    public static string EscapeTarget(string path) =>
        path.Replace(" ", "%20", StringComparison.Ordinal);

    /// <summary>
    /// Removes the image reference this policy wrote for an attachment from a note's text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An attached image is now shown as an image on the note, so the Markdown that used to stand
    /// in for it is noise: forty characters of percent-escaped path sitting where the user expected
    /// their screenshot.
    /// </para>
    /// <para>
    /// Only a reference pointing at the given attachment path is removed. A reference the user
    /// typed themselves points somewhere else and survives, which is what keeps this from being a
    /// silent edit of their words. The line break that followed the reference goes with it, or
    /// removing an image would leave a blank line behind every time.
    /// </para>
    /// </remarks>
    public static string RemoveImageReference(string content, string path)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrWhiteSpace(path))
        {
            return content;
        }

        var targets = new[] { path, EscapeTarget(path) }
            .Distinct(StringComparer.Ordinal)
            .Select(Regex.Escape);

        var pattern = $@"!\[[^\]]*\]\((?:{string.Join('|', targets)})\)[ \t]*(?:\r?\n)?";

        return Regex.Replace(
            content,
            pattern,
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }
}
