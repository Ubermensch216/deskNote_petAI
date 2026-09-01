using System.Globalization;
using System.Security.Cryptography;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.App.Services;

/// <summary>An attachment that has been stored and is ready to be referenced from a note.</summary>
/// <param name="Attachment">The stored record.</param>
/// <param name="Markdown">Text to insert into the note body.</param>
public readonly record struct StoredAttachment(Attachment Attachment, string Markdown);

/// <summary>
/// Puts attachment bytes somewhere durable and records them against a note.
/// </summary>
/// <remarks>
/// <para>
/// Embedded files are stored per note under the app's data directory and named by content hash,
/// so pasting the same screenshot into a note twice costs one copy rather than two.
/// </para>
/// <para>
/// Linked files are never copied. What is stored is the path the user already chose, which is what
/// report p5 asks for with 대용량은 복사보다 링크 우선 — at the cost, stated plainly, that moving
/// the original later breaks the link.
/// </para>
/// </remarks>
public sealed class AttachmentStore(IAttachmentRepository attachments, IClock clock)
{
    public static string RootDirectory { get; } = Path.Combine(AppPaths.Root, "attachments");

    /// <summary>Copies bytes into the note's attachment folder and records them.</summary>
    public async Task<StoredAttachment> EmbedAsync(
        Guid noteId,
        string displayName,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var extension = Path.GetExtension(displayName);
        var folder = Path.Combine(RootDirectory, noteId.ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, hash + extension);
        if (!File.Exists(path))
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        }

        return await RecordAsync(
            noteId,
            AttachmentKind.Embedded,
            path,
            displayName,
            bytes.LongLength,
            hash,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records a file where it already sits, without copying it.</summary>
    public async Task<StoredAttachment> LinkAsync(
        Guid noteId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(sourcePath);

        return await RecordAsync(
            noteId,
            AttachmentKind.Link,
            sourcePath,
            info.Name,
            info.Exists ? info.Length : 0,
            sha256: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies <see cref="AttachmentPolicy"/> to a file the user dropped.</summary>
    public async Task<StoredAttachment> AddFileAsync(
        Guid noteId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(sourcePath);
        var decision = AttachmentPolicy.ForFile(info.Name, info.Exists ? info.Length : 0);

        if (decision.Kind == AttachmentKind.Link)
        {
            return await LinkAsync(noteId, sourcePath, cancellationToken).ConfigureAwait(false);
        }

        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return await EmbedAsync(noteId, info.Name, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Everything attached to a note, oldest first.</summary>
    public Task<IReadOnlyList<Attachment>> ListAsync(
        Guid noteId,
        CancellationToken cancellationToken = default) =>
        attachments.ListForNoteAsync(noteId, cancellationToken);

    /// <summary>
    /// Detaches one attachment: the record goes, and so do the bytes if this app owns them.
    /// </summary>
    /// <remarks>
    /// Embedded files are named by content hash, so the same screenshot attached to a note twice
    /// is one file with two records. The bytes are only deleted once nothing else in the note
    /// points at them; otherwise removing the second copy would blank the first. A linked file is
    /// the user's own, sitting where they put it, and is never deleted.
    /// </remarks>
    public async Task RemoveAsync(Guid noteId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attached = await attachments.ListForNoteAsync(noteId, cancellationToken).ConfigureAwait(false);

        if (attached.FirstOrDefault(candidate => candidate.Id == attachmentId) is not { } attachment)
        {
            return;
        }

        await attachments.DeleteAsync(attachmentId, cancellationToken).ConfigureAwait(false);

        if (attachment.Kind != AttachmentKind.Embedded)
        {
            return;
        }

        var sharedWithAnother = attached.Any(other =>
            other.Id != attachmentId
            && string.Equals(other.Path, attachment.Path, StringComparison.OrdinalIgnoreCase));

        if (sharedWithAnother)
        {
            return;
        }

        try
        {
            File.Delete(attachment.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The record is gone, so the note no longer shows it. A file left behind costs disk,
            // not correctness, and the note's folder is removed wholesale when the note is deleted.
            CrashLog.Write($"Could not delete attachment file {attachment.Path}", ex);
        }
    }

    /// <summary>Deletes a note's embedded files. Linked originals are left alone.</summary>
    public void DeleteEmbeddedFiles(Guid noteId)
    {
        var folder = Path.Combine(RootDirectory, noteId.ToString("N", CultureInfo.InvariantCulture));

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write($"Could not remove attachment folder for note {noteId}", ex);
        }
    }

    private async Task<StoredAttachment> RecordAsync(
        Guid noteId,
        AttachmentKind kind,
        string path,
        string displayName,
        long sizeBytes,
        string? sha256,
        CancellationToken cancellationToken)
    {
        var attachment = new Attachment
        {
            Id = Guid.CreateVersion7(),
            NoteId = noteId,
            Kind = kind,
            Path = path,
            DisplayName = displayName,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            CreatedAt = clock.UtcNow,
        };

        await attachments.AddAsync(attachment, cancellationToken).ConfigureAwait(false);

        var markdown = AttachmentPolicy.ToMarkdown(
            displayName,
            path,
            AttachmentPolicy.IsImage(displayName));

        return new StoredAttachment(attachment, markdown);
    }
}
