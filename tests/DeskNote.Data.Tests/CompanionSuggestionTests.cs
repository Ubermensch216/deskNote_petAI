using DeskNote.Companion.Core;
using DeskNote.Core.Models;

namespace DeskNote.Data.Tests;

public class CompanionSuggestionTests
{
    [Fact]
    public async Task Upcoming_reminder_is_offered_once_within_twenty_four_hours()
    {
        await using var database = await TestDatabase.CreateAsync();
        var profile = new SqliteCompanionRepository(database.Factory, new RewardPolicy(), new CarePolicy());
        await profile.GetOrCreateAsync(TestContext.Current.CancellationToken);

        var note = new Note
        {
            Id = Guid.NewGuid(),
            Title = "Review",
            Content = "Before the meeting",
        };
        await database.Notes.AddAsync(note, TestContext.Current.CancellationToken);
        var now = new DateTimeOffset(2026, 8, 31, 2, 0, 0, TimeSpan.Zero);
        var reminders = new SqliteReminderRepository(database.Factory);
        await reminders.AddAsync(
            new Reminder
            {
                Id = Guid.NewGuid(),
                NoteId = note.Id,
                DueAt = now.AddMinutes(20),
            },
            TestContext.Current.CancellationToken);

        var service = new SqliteCompanionSuggestionService(database.Factory);
        var first = await service.TryOfferAsync(now, TestContext.Current.CancellationToken);
        var duplicate = await service.TryOfferAsync(now.AddMinutes(10), TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.Equal(CompanionSuggestionType.UpcomingReminder, first.Type);
        Assert.Equal(note.Id, first.NoteId);
        Assert.Null(duplicate);
    }
}
