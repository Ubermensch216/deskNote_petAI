using System.Globalization;
using DeskNote.Core.Models;

namespace DeskNote.Benchmarks;

/// <summary>
/// Generates a synthetic note library to measure against.
/// </summary>
/// <remarks>
/// <para>
/// Mixed Korean and English on purpose. The trigram FTS index behaves very differently for the
/// two — Korean produces far more distinct trigrams per character — so a corpus of one language
/// would give a search number that does not predict the other.
/// </para>
/// <para>
/// Nothing here is real user data, which is also what report p18 asks for: AI and performance
/// fixtures use synthetic work, meeting and TODO content rather than anyone's actual notes.
/// </para>
/// </remarks>
public static class NoteCorpus
{
    private static readonly string[] KoreanSubjects =
    [
        "수도관 누수 점검", "API 응답속도 개선", "회의록 정리", "릴리스 노트 작성",
        "배포 일정 조율", "장애 원인 분석", "예산 검토", "채용 면접 준비",
        "고객 요청 정리", "설계 검토 의견", "테스트 케이스 보강", "문서 최신화",
    ];

    private static readonly string[] EnglishSubjects =
    [
        "Review the deployment plan", "Investigate the latency spike", "Draft the release notes",
        "Prepare the sprint demo", "Update the onboarding guide", "Check the backup restore",
        "Refactor the search index", "Follow up with the vendor",
    ];

    private static readonly string[] Tags =
    [
        "backend", "frontend", "회의록", "설비", "v1", "urgent", "개인", "release",
    ];

    private static readonly string[] KoreanTasks =
    [
        "p95 기준 확정", "GPU fallback 테스트", "담당자 지정", "일정 재확인", "문서 링크 추가",
    ];

    private static readonly string[] EnglishTasks =
    [
        "confirm the rollback plan", "add a regression test", "ping the on-call", "measure cold start",
    ];

    /// <summary>Builds a deterministic corpus, so two runs measure the same work.</summary>
    public static IEnumerable<Note> Generate(int count, DateTimeOffset start, int seed = 20260829)
    {
        var random = new Random(seed);

        for (var i = 0; i < count; i++)
        {
            var korean = random.Next(2) == 0;
            var subject = korean
                ? KoreanSubjects[random.Next(KoreanSubjects.Length)]
                : EnglishSubjects[random.Next(EnglishSubjects.Length)];

            yield return new Note
            {
                Id = Guid.CreateVersion7(),
                Title = random.Next(3) == 0 ? $"{subject} #{i}" : string.Empty,
                Content = BuildContent(random, subject, korean, i),
                ColorKey = NoteColors.All[random.Next(NoteColors.All.Count)],
                // Only a handful are on the desktop; the rest live in the library, which is the
                // shape a real user's collection takes after a few months.
                IsOpen = i < 8,
                CreatedAt = start.AddMinutes(-i),
                UpdatedAt = start.AddMinutes(-i),
            };
        }
    }

    private static string BuildContent(Random random, string subject, bool korean, int index)
    {
        var lines = new List<string> { subject };

        var paragraphs = random.Next(1, 4);
        for (var i = 0; i < paragraphs; i++)
        {
            lines.Add(korean
                ? $"{KoreanSubjects[random.Next(KoreanSubjects.Length)]} 관련 내용 {index}-{i} 을 확인했다."
                : $"Noted {EnglishSubjects[random.Next(EnglishSubjects.Length)].ToLowerInvariant()} ({index}-{i}).");
        }

        if (random.Next(2) == 0)
        {
            lines.Add(string.Empty);
            var items = random.Next(2, 5);
            for (var i = 0; i < items; i++)
            {
                var done = random.Next(3) == 0 ? 'x' : ' ';
                var task = korean
                    ? KoreanTasks[random.Next(KoreanTasks.Length)]
                    : EnglishTasks[random.Next(EnglishTasks.Length)];
                lines.Add($"- [{done}] {task} {i.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        if (random.Next(2) == 0)
        {
            lines.Add(string.Empty);
            var count = random.Next(1, 3);
            var chosen = Enumerable.Range(0, count).Select(_ => Tags[random.Next(Tags.Length)]).Distinct();
            lines.Add(string.Join(' ', chosen.Select(t => '#' + t)));
        }

        return string.Join('\n', lines);
    }
}
