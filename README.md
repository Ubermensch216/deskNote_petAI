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

## 데이터 위치

`%LOCALAPPDATA%\DeskNote\notes.db` (WAL). 로그는 `%LOCALAPPDATA%\DeskNote\logs\desknote.log`.

DB를 OneDrive 같은 파일 동기화 폴더에 두지 않는다. WAL 사용 중 파일 단위 외부 동기화는
손상 위험이 있어, 동기화는 이후 note-level operation 복제로 구현한다 (보고서 p6).
