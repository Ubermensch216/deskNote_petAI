using System.Collections.Concurrent;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class AutosaveSchedulerTests
{
    /// <summary>Short enough to keep tests quick, long enough that coalescing is observable.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(80);

    private sealed record Save(Guid NoteId, string Title, string Content);

    private sealed class Recorder
    {
        public ConcurrentQueue<Save> Saves { get; } = new();

        public Func<Save, Task>? OnSave { get; set; }

        public Task WriteAsync(Guid id, string title, string content, CancellationToken _)
        {
            var save = new Save(id, title, content);
            Saves.Enqueue(save);
            return OnSave?.Invoke(save) ?? Task.CompletedTask;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task The_report_default_debounce_sits_in_the_specified_band()
    {
        // Report p5: autosave starts 250-500 ms after the user stops typing.
        Assert.InRange(AutosaveScheduler.DefaultDebounce.TotalMilliseconds, 250, 500);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_pause_in_typing_triggers_one_save()
    {
        var recorder = new Recorder();
        await using var scheduler = new AutosaveScheduler(recorder.WriteAsync, Debounce);
        var noteId = Guid.NewGuid();

        scheduler.Schedule(noteId, "제목", "내용");
        await WaitForAsync(() => !recorder.Saves.IsEmpty);

        Assert.Single(recorder.Saves);
        Assert.Equal(new Save(noteId, "제목", "내용"), recorder.Saves.First());
    }

    /// <summary>
    /// The point of the debounce: a burst of keystrokes must produce one write carrying the final
    /// text, not one write per character.
    /// </summary>
    [Fact]
    public async Task Rapid_keystrokes_collapse_into_a_single_save_of_the_final_text()
    {
        var recorder = new Recorder();
        await using var scheduler = new AutosaveScheduler(recorder.WriteAsync, Debounce);
        var noteId = Guid.NewGuid();

        foreach (var text in new[] { "회", "회의", "회의 ", "회의 준", "회의 준비" })
        {
            scheduler.Schedule(noteId, string.Empty, text);
            await Task.Delay(10);
        }

        await WaitForAsync(() => !recorder.Saves.IsEmpty);
        await Task.Delay(Debounce * 2);

        Assert.Single(recorder.Saves);
        Assert.Equal("회의 준비", recorder.Saves.First().Content);
    }

    [Fact]
    public async Task Two_notes_debounce_independently()
    {
        var recorder = new Recorder();
        await using var scheduler = new AutosaveScheduler(recorder.WriteAsync, Debounce);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        scheduler.Schedule(first, string.Empty, "첫 번째");
        scheduler.Schedule(second, string.Empty, "두 번째");
        await WaitForAsync(() => recorder.Saves.Count == 2);

        Assert.Equal(2, recorder.Saves.Count);
        Assert.Contains(recorder.Saves, s => s.NoteId == first && s.Content == "첫 번째");
        Assert.Contains(recorder.Saves, s => s.NoteId == second && s.Content == "두 번째");
    }

    /// <summary>
    /// Closing a note must not lose the last few keystrokes to a debounce that had not elapsed.
    /// </summary>
    [Fact]
    public async Task Flush_writes_immediately_without_waiting_for_the_debounce()
    {
        var recorder = new Recorder();
        await using var scheduler = new AutosaveScheduler(recorder.WriteAsync, TimeSpan.FromSeconds(30));
        var noteId = Guid.NewGuid();

        scheduler.Schedule(noteId, "제목", "닫기 직전 입력");
        Assert.True(scheduler.HasPendingWork);

        await scheduler.FlushAsync(noteId);

        Assert.Single(recorder.Saves);
        Assert.Equal("닫기 직전 입력", recorder.Saves.First().Content);
        Assert.False(scheduler.HasPendingWork);
    }

    /// <summary>A flush must also cancel the timer, or the same text gets written twice.</summary>
    [Fact]
    public async Task Flushing_cancels_the_pending_timer()
    {
        var recorder = new Recorder();
        await using var scheduler = new AutosaveScheduler(recorder.WriteAsync, Debounce);
        var noteId = Guid.NewGuid();

        scheduler.Schedule(noteId, string.Empty, "한 번만");
        await scheduler.FlushAsync(noteId);
        await Task.Delay(Debounce * 3);

        Assert.Single(recorder.Saves);
    }

    [Fact]
    public async Task Flushing_a_note_with_nothing_pending_does_nothing()
    {
        var recorder = new Recorder();
        await using var scheduler = new AutosaveScheduler(recorder.WriteAsync, Debounce);

        await scheduler.FlushAsync(Guid.NewGuid());

        Assert.Empty(recorder.Saves);
    }

    [Fact]
    public async Task Flush_all_writes_every_pending_note()
    {
        var recorder = new Recorder();
        await using var scheduler = new AutosaveScheduler(recorder.WriteAsync, TimeSpan.FromSeconds(30));

        for (var i = 0; i < 5; i++)
        {
            scheduler.Schedule(Guid.NewGuid(), string.Empty, $"메모 {i}");
        }

        await scheduler.FlushAllAsync();

        Assert.Equal(5, recorder.Saves.Count);
        Assert.False(scheduler.HasPendingWork);
    }

    /// <summary>
    /// A failing write must not escape into the UI thread. The user's text is still on screen and
    /// the next keystroke schedules another attempt, so the app reports rather than crashes.
    /// </summary>
    [Fact]
    public async Task A_failing_save_is_reported_rather_than_thrown()
    {
        var recorder = new Recorder
        {
            OnSave = _ => throw new InvalidOperationException("database is locked"),
        };

        Exception? reported = null;
        await using var scheduler = new AutosaveScheduler(recorder.WriteAsync, Debounce);
        scheduler.SaveFailed += (_, ex) => reported = ex;

        scheduler.Schedule(Guid.NewGuid(), string.Empty, "저장 실패");
        await WaitForAsync(() => reported is not null);

        Assert.IsType<InvalidOperationException>(reported);
    }

    [Fact]
    public async Task Disposing_flushes_outstanding_work()
    {
        var recorder = new Recorder();
        var scheduler = new AutosaveScheduler(recorder.WriteAsync, TimeSpan.FromSeconds(30));

        scheduler.Schedule(Guid.NewGuid(), string.Empty, "종료 직전");
        await scheduler.DisposeAsync();

        Assert.Single(recorder.Saves);
        Assert.Equal("종료 직전", recorder.Saves.First().Content);
    }
}
