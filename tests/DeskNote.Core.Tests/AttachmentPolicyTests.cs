using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class AttachmentPolicyTests
{
    [Theory]
    [InlineData("shot.png")]
    [InlineData("photo.JPG")]
    [InlineData("diagram.webp")]
    [InlineData("scan.tiff")]
    public void Image_files_are_recognized(string fileName)
    {
        Assert.True(AttachmentPolicy.IsImage(fileName));
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void Other_files_are_not_images(string fileName)
    {
        Assert.False(AttachmentPolicy.IsImage(fileName));
    }

    [Fact]
    public void A_small_image_is_copied_into_the_note()
    {
        var decision = AttachmentPolicy.ForFile("shot.png", 400 * 1024);

        Assert.Equal(AttachmentKind.Embedded, decision.Kind);
    }

    /// <summary>Report p5: 대용량은 복사보다 링크 우선.</summary>
    [Fact]
    public void A_large_image_is_linked_rather_than_duplicated()
    {
        var decision = AttachmentPolicy.ForFile("panorama.png", AttachmentPolicy.EmbedSizeLimitBytes + 1);

        Assert.Equal(AttachmentKind.Link, decision.Kind);
    }

    [Fact]
    public void A_non_image_is_linked_even_when_small()
    {
        var decision = AttachmentPolicy.ForFile("report.pdf", 10 * 1024);

        Assert.Equal(AttachmentKind.Link, decision.Kind);
    }

    /// <summary>
    /// Clipboard bytes have no path to link to, so they are copied whatever their size — the
    /// alternative is losing the screenshot as soon as the clipboard changes.
    /// </summary>
    [Fact]
    public void A_pasted_image_is_always_copied()
    {
        Assert.Equal(AttachmentKind.Embedded, AttachmentPolicy.ForClipboardImage().Kind);
    }

    [Fact]
    public void The_size_limit_is_the_boundary_itself()
    {
        Assert.Equal(
            AttachmentKind.Embedded,
            AttachmentPolicy.ForFile("shot.png", AttachmentPolicy.EmbedSizeLimitBytes).Kind);
    }

    [Fact]
    public void Images_use_image_markdown_and_other_files_use_links()
    {
        Assert.Equal(
            "![shot.png](C:/notes/shot.png)",
            AttachmentPolicy.ToMarkdown("shot.png", "C:/notes/shot.png", isImage: true));

        Assert.Equal(
            "[report.pdf](C:/docs/report.pdf)",
            AttachmentPolicy.ToMarkdown("report.pdf", "C:/docs/report.pdf", isImage: false));
    }

    /// <summary>An unescaped space ends the target early and produces a link to the wrong path.</summary>
    [Fact]
    public void Spaces_in_a_path_are_escaped()
    {
        var markdown = AttachmentPolicy.ToMarkdown("회의 자료.pdf", @"C:\My Notes\회의 자료.pdf", isImage: false);

        Assert.DoesNotContain(") ", markdown, StringComparison.Ordinal);
        Assert.Contains("%20", markdown, StringComparison.Ordinal);
        Assert.EndsWith(")", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// A decision carries what to do and nothing else.
    /// </summary>
    /// <remarks>
    /// It used to carry a sentence explaining itself, written for a screen that was never built —
    /// so nothing ever read it, and the only thing it did was put Korean prose in a project that
    /// is meant to hold no user-facing text at all. If a future screen wants to explain the
    /// decision, it can say it in the language the user is reading.
    /// </remarks>
    [Fact]
    public void A_decision_is_only_what_to_do()
    {
        Assert.Equal(
            AttachmentKind.Embedded,
            AttachmentPolicy.ForFile("shot.png", 1024).Kind);
        Assert.Equal(
            AttachmentKind.Link,
            AttachmentPolicy.ForFile("big.png", AttachmentPolicy.EmbedSizeLimitBytes * 2).Kind);
        Assert.Equal(AttachmentKind.Embedded, AttachmentPolicy.ForClipboardImage().Kind);
    }

    /// <summary>
    /// The note shows the image itself, so the reference that stood in for it is removed rather
    /// than left in the middle of the user's sentence.
    /// </summary>
    [Fact]
    public void An_images_reference_is_removed_from_the_text()
    {
        const string path = @"C:\store\a1b2.png";
        var content = "위\n" + AttachmentPolicy.ToMarkdown("shot.png", path, isImage: true) + "\n아래";

        Assert.Equal("위\n아래", AttachmentPolicy.RemoveImageReference(content, path));
    }

    /// <summary>The reference is written with the path escaped, and is removed with it escaped.</summary>
    [Fact]
    public void A_reference_to_a_path_with_spaces_is_removed()
    {
        const string path = @"C:\My Notes\회의 자료.png";
        var content = AttachmentPolicy.ToMarkdown("회의 자료.png", path, isImage: true) + "\r\n적어둘 것";

        Assert.Equal("적어둘 것", AttachmentPolicy.RemoveImageReference(content, path));
    }

    /// <summary>
    /// Only what this policy wrote for that attachment goes. An image the user linked themselves
    /// is their text, and removing it would be an edit they did not ask for.
    /// </summary>
    [Fact]
    public void Another_images_reference_is_left_alone()
    {
        const string content = "![theirs](C:/elsewhere/photo.png)";

        Assert.Equal(content, AttachmentPolicy.RemoveImageReference(content, @"C:\store\a1b2.png"));
    }

    /// <summary>A file link to the same path is not an image reference and is not what is shown.</summary>
    [Fact]
    public void A_plain_link_to_the_same_file_survives()
    {
        const string path = @"C:\store\report.png";
        var content = AttachmentPolicy.ToMarkdown("report.png", path, isImage: false);

        Assert.Equal(content, AttachmentPolicy.RemoveImageReference(content, path));
    }

    [Fact]
    public void Removing_a_reference_from_text_that_has_none_changes_nothing()
    {
        Assert.Equal("그냥 메모", AttachmentPolicy.RemoveImageReference("그냥 메모", @"C:\store\a1b2.png"));
        Assert.Equal(string.Empty, AttachmentPolicy.RemoveImageReference(string.Empty, @"C:\store\a1b2.png"));
    }
}
