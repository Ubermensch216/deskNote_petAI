# DeskNote

Windows 로컬 AI 스티키 위젯. 제품 정의는
[기획 보고서](docs/Windows%20로컬%20AI%20스티키%20위젯%20제품·기술%20기획%20보고서.pdf)를 따른다.

핵심 원칙은 보고서 p21의 우선순위다: **메모 → 기억 → AI → 자동화**.
AI는 어느 단계에서도 메모 앱의 필수 의존성이 되지 않는다. 모델을 받지 않아도 메모 기능은
100% 동작하고, 모델을 지워도 데이터는 온전하다.

## 프로젝트 구조

| 프로젝트 | 대상 | 역할 |
|---|---|---|
| `src/DeskNote.Core` | net10.0 | 도메인 모델, 인터페이스, UI·DB에 의존하지 않는 정책 |
| `src/DeskNote.Data` | net10.0 | SQLite + FTS5, 마이그레이션, 리포지토리 |
| `src/DeskNote.App` | net10.0-windows | WinUI 3 데스크톱 UI (실행 진입점) |
| `tests/DeskNote.Core.Tests` | net10.0 | 도메인·배치·자동저장 정책 |
| `tests/DeskNote.Data.Tests` | net10.0 | 영속성, 한국어 FTS, 마이그레이션 |

의존 방향은 `App → Core ← Data`. `Core`는 아무것도 참조하지 않는다.
AI 계층은 `Core`의 `ILocalAiService` 뒤에만 존재하며, 현재는 항상 "사용 불가"를 반환하는
`NullAiService`가 주입돼 있다.

## 빌드와 실행

```bash
dotnet build
```

```bash
dotnet run --project src/DeskNote.App
```

Visual Studio는 필요 없다. WinUI 3의 XAML 컴파일러와 Windows SDK 빌드 도구는 NuGet에서 온다.
앱은 unpackaged + Windows App SDK self-contained로 빌드되므로 별도 런타임 설치도 필요 없다.

## 테스트

```bash
dotnet test
```

## 편집 단축키

| 키 | 동작 |
|---|---|
| `Ctrl+B` / `Ctrl+I` | 굵게 / 기울임 |
| `Ctrl+Shift+K` | 인라인 코드 |
| `Ctrl+K` | 링크 |
| `Ctrl+1` / `Ctrl+2` / `Ctrl+3` | 제목 1·2·3 |
| `Ctrl+Shift+L` | 글머리 목록 |
| `Ctrl+Shift+C` | 체크리스트 |
| `Ctrl+Enter` | 커서 줄의 체크박스 토글 |
| `Enter` | 목록 안에서 다음 항목 이어쓰기 (빈 항목에서는 목록 종료) |

메모 본문은 Markdown 원문 그대로 저장된다. 체크리스트도 본문 안의 `- [ ]` 가 유일한 원본이며,
`checklist_items` 테이블은 저장 시 본문에서 다시 만들어지는 조회용 투영이다.

## 메모 라이브러리

노트의 `⋯` 메뉴에서 **메모 라이브러리 / Notes Explorer** 를 연다. 전체 메모를 노트북·태그·삭제
여부로 좁혀 보고, 검색창에 입력하면 150ms 뒤 FTS 검색이 돈다. 행을 한 번 누르면 해당 메모가
바탕화면에 뜬다.

태그는 본문 안의 `#backend` 처럼 쓴 해시태그가 원본이고, `tags`/`note_tags` 테이블은 저장 시
본문에서 다시 만들어지는 조회용 투영이다. Markdown 제목(`# 제목`)과 구분하기 위해 `#` 바로 뒤에
공백이나 `#` 이 오면 태그로 보지 않는다.

## 알림

노트의 `⋯` → **알림 / Remind me** 에서 10분 뒤·1시간 뒤·내일 아침 9시를 고른다. 30초마다
확인하므로 절전/최대 절전에서 깨어난 뒤에도 놓치지 않는다. 반복 알림은 RFC 5545 `RRULE` 의
부분집합(`FREQ=DAILY|WEEKLY|MONTHLY|YEARLY`, `INTERVAL`)을 쓰며, 자리를 비운 사이 지나간 반복은
하나로 합쳐 다음 회차만 알린다.

## 첨부

이미지를 붙여넣으면 메모 폴더로 복사되고, 파일을 끌어다 놓으면 10MB 이하 이미지만 복사하고
나머지는 원본 위치를 링크한다 (보고서 p5: 대용량은 복사보다 링크 우선). 어느 쪽이든 본문에는
Markdown 참조가 삽입되므로 `Ctrl+Z` 로 되돌릴 수 있다.

## 데이터 위치

`%LOCALAPPDATA%\DeskNote\notes.db` (WAL). 로그는 `%LOCALAPPDATA%\DeskNote\logs\desknote.log`.

DB를 OneDrive 같은 파일 동기화 폴더에 두지 않는다. WAL 사용 중 파일 단위 외부 동기화는
손상 위험이 있어, 동기화는 이후 note-level operation 복제로 구현한다 (보고서 p6).
