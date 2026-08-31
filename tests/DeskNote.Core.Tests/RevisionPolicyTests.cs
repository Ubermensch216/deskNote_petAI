using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

/// <summary>
/// Guards the rule that makes an AI rewrite recoverable.
/// </summary>
/// <remarks>
/// Report p15 requires the original to survive an AI rewrite. That is only true if the rewrite is
/// classified as a bulk replacement — routed through the ordinary typing path it inherits the
/// five-minute checkpoint interval, and a second rewrite inside that window replaces the note
/// with nothing kept. These tests pin the difference between the two paths.
/// </remarks>
public class RevisionPolicyTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Typing_checkpoints_once_per_interval()
    {
        var policy = new RevisionPolicy(TimeSpan.FromMinutes(5));
        var note = Guid.CreateVersion7();

        Assert.True(policy.ShouldSnapshot(note, "이전 본문", Noon, RevisionReason.Edit));
        policy.MarkSnapshotTaken(note, Noon);

        Assert.False(policy.ShouldSnapshot(note, "이전 본문", Noon.AddMinutes(2), RevisionReason.Edit));
        Assert.True(policy.ShouldSnapshot(note, "이전 본문", Noon.AddMinutes(5), RevisionReason.Edit));
    }

    /// <summary>The case the interval would otherwise swallow: two AI actions in quick succession.</summary>
    [Fact]
    public void A_bulk_replacement_checkpoints_even_inside_the_interval()
    {
        var policy = new RevisionPolicy(TimeSpan.FromMinutes(5));
        var note = Guid.CreateVersion7();

        policy.MarkSnapshotTaken(note, Noon);

        Assert.True(policy.ShouldSnapshot(note, "이전 본문", Noon.AddSeconds(1), RevisionReason.BulkReplace));
    }

    [Fact]
    public void An_empty_note_has_nothing_worth_keeping()
    {
        var policy = new RevisionPolicy();

        Assert.False(policy.ShouldSnapshot(Guid.CreateVersion7(), "", Noon, RevisionReason.BulkReplace));
    }

    /// <summary>
    /// The history list reads these two fields to say "AI · 요약" rather than "직접 편집", which is
    /// the difference between a version a user can place and one they cannot.
    /// </summary>
    [Fact]
    public void A_revision_records_which_ai_action_produced_it()
    {
        var noteId = Guid.CreateVersion7();

        var revision = RevisionPolicy.CreateRevision(
            noteId,
            "이전 본문",
            Noon,
            RevisionSource.Ai,
            "RewriteFormal");

        Assert.Equal(noteId, revision.NoteId);
        Assert.Equal("이전 본문", revision.Content);
        Assert.Equal(RevisionSource.Ai, revision.Source);
        Assert.Equal("RewriteFormal", revision.ActionName);
    }

    [Fact]
    public void A_revision_is_the_users_own_edit_unless_told_otherwise()
    {
        var revision = RevisionPolicy.CreateRevision(Guid.CreateVersion7(), "이전 본문", Noon);

        Assert.Equal(RevisionSource.User, revision.Source);
        Assert.Null(revision.ActionName);
    }

    /// <summary>A note closed and reopened should checkpoint on its next edit, not inherit a timer.</summary>
    [Fact]
    public void Forgetting_a_note_restarts_its_interval()
    {
        var policy = new RevisionPolicy(TimeSpan.FromMinutes(5));
        var note = Guid.CreateVersion7();

        policy.MarkSnapshotTaken(note, Noon);
        Assert.False(policy.ShouldSnapshot(note, "이전 본문", Noon.AddMinutes(1), RevisionReason.Edit));

        policy.Forget(note);
        Assert.True(policy.ShouldSnapshot(note, "이전 본문", Noon.AddMinutes(1), RevisionReason.Edit));
    }
}
