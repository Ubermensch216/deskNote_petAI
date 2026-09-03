using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Data;

// Seeds a throwaway database for the screen photographs in docs/images/.
//
// The notes below are invented. Nothing here reads or copies a real database: the point of this
// tool is that nobody's actual notes end up in the README. Run the app against the same folder
// with `--data <path>` to photograph it.
//
//   dotnet run --project tools/DemoSeeder -- <data directory>

var root = args.Length > 0
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("Pass the data directory to seed, the same one the app gets with --data.");

Directory.CreateDirectory(root);

var clock = new SystemClock();
var connections = new SqliteConnectionFactory(Path.Combine(root, "notes.db"));
var migration = await new MigrationRunner(connections).MigrateAsync();
Console.WriteLine($"schema {migration.FromVersion} -> {migration.ToVersion}");

var notes = new SqliteNoteRepository(connections, clock);
var library = new SqliteNoteLibrary(connections, clock);
var settings = new SqliteSettingsStore(connections);

// The pet is off by default, and the dashboard is one of the things being photographed.
await settings.SetAsync(SettingKeys.CompanionEnabled, "true");
await settings.SetAsync(SettingKeys.CompanionSelectedPet, "otter");
await settings.SetAsync(SettingKeys.CompanionPetName, "Bori");

// A walking pet moves out from under the pointer between reading its position and
// clicking it, so the demo instance stands still. It is the same still frame a user sees
// with 움직이기 turned off.
await settings.SetAsync(SettingKeys.CompanionReduceMotion, "true");

var payments = await library.CreateNotebookAsync("결제 시스템");
var onboarding = await library.CreateNotebookAsync("온보딩");

var now = clock.UtcNow;
var seeded = new (string Content, string Color, TimeSpan Age, Guid? Notebook, bool Open)[]
{
    ("""
     결제 모듈 리팩터링 회의
     - 3분기에 페이먼트 모듈 정리 예정
     - [ ] 재시도 정책 문서로 옮기기
     - [x] 장애 회고 링크 모으기
     #결제 #회의
     """, NoteColors.Yellow, TimeSpan.FromMinutes(7), payments.Id, true),

    ("""
     페이먼트 모듈 정리 메모
     결제 실패 시 재시도는 3회, 지수 백오프. 카드사 응답 코드별 분기는 아직 문서가 없다.
     #결제
     """, NoteColors.Teal, TimeSpan.FromDays(1), payments.Id, true),

    ("""
     카드 결제 장애 회고
     - [ ] 알림 임계값 다시 정하기
     타임아웃이 30초로 잡혀 있어 사용자가 두 번 눌렀다.
     #결제 #장애
     """, NoteColors.Green, TimeSpan.FromDays(1), payments.Id, false),

    ("""
     새 팀원 온보딩 체크리스트
     - [x] 계정 발급
     - [x] 저장소 접근 권한
     - [ ] 첫 주 페어링 일정
     #온보딩
     """, NoteColors.Blue, TimeSpan.FromDays(2), onboarding.Id, false),

    ("""
     # 회의 준비
     다음 주 화요일 오전 10시 · 분기 계획
     #회의
     """, NoteColors.Amber, TimeSpan.FromHours(3), null, true),

    ("""
     아이디어 — 검색 결과에 근거 표시
     답을 믿을 수 있으려면 어디서 나왔는지가 같이 보여야 한다.
     #아이디어
     """, NoteColors.Purple, TimeSpan.FromDays(4), null, false),

    ("""
     전화 메모 — 김대리
     정산 주기 변경 건, 다음 주까지 회신 필요
     #결제
     """, NoteColors.Pink, TimeSpan.FromDays(9), null, false),
};

var index = 0;
foreach (var (content, color, age, notebook, open) in seeded)
{
    var created = now - age;
    var note = new Note
    {
        Id = Guid.CreateVersion7(),
        Title = NoteContent.DeriveTitle(content),
        Content = content,
        ColorKey = color,
        NotebookId = notebook,
        IsOpen = open,
        Geometry = NoteGeometry.ForPreset(NoteSizePreset.Medium, 80 + (index * 40), 80 + (index * 30), string.Empty),
        CreatedAt = created,
        UpdatedAt = created,
    };

    await notes.AddAsync(note);

    // AddAsync stores the row; the content write is what builds the FTS, tag and checklist
    // projections, so the library and search have something to show.
    await notes.UpdateContentAsync(note.Id, note.Title, note.Content);
    await notes.SetOpenAsync(note.Id, open);
    if (notebook is { } notebookId)
    {
        await notes.SetNotebookAsync(note.Id, notebookId);
    }

    index++;
}

Console.WriteLine($"seeded {seeded.Length} notes into {root}");
