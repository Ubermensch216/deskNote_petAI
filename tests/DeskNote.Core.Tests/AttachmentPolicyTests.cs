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

    [Fact]
    public void Every_decision_carries_a_reason_the_ui_can_show()
    {
        Assert.NotEmpty(AttachmentPolicy.ForFile("shot.png", 1024).Reason);
        Assert.NotEmpty(AttachmentPolicy.ForFile("big.png", AttachmentPolicy.EmbedSizeLimitBytes * 2).Reason);
        Assert.NotEmpty(AttachmentPolicy.ForClipboardImage().Reason);
    }
}
