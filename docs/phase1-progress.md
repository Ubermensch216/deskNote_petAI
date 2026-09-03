# Phase 1 진행 기록

> 이 문서는 2026-08-31 시점의 **기록**이다. 수치와 파일 크기는 그날의 것이며, 현재 구조는 [architecture.md](architecture.md)를 본다.

기준 브랜치: `feature/phase0-foundation`  
기록일: 2026-08-31

## 완료

- `App.xaml.cs`를 55줄의 WinUI 진입점으로 축소
- `CompositionRoot`로 객체 조립 분리
- `ApplicationRuntime`으로 시작·종료 순서와 서비스 소유권 분리
- 종료 시 자동 저장 flush 보장
- `NoteSaveResult`로 저장 후 비동기 활동 분류 접점 추가
- `NoteOpenContext`와 `NoteOpened`로 검색·관련 메모·AI 근거·알림 진입점 구분
- `NoteWindow.Ai.cs`로 AI UI 책임 분리
- `NoteWindowManager.Memory.cs`로 라이브러리·브리핑·검색 표면 분리

## 크기 변화

| 파일 | 이전 | 현재 |
|---|---:|---:|
| `App.xaml.cs` | 약 312줄 | 55줄 |
| `NoteWindow.xaml.cs` | 1,673줄 | 1,124줄 |
| `NoteWindow.Ai.cs` | 없음 | 562줄 |
| `NoteWindowManager.cs` | 약 757줄 | 429줄 |
| `NoteWindowManager.Memory.cs` | 없음 | 363줄 |

## 검증

- `dotnet format --verify-no-changes`: 통과
- Release App build: 경고 0, 오류 0
- 전체 테스트: 504/504 통과
- win-x64 single-file 생성: 통과(당시 실제 시작 검사는 누락, 2026-09-01 폴더형 패키지로 보정)

## 다음 순서

1. 설정 화면의 최소 뼈대와 companion feature flag 계약
2. `DeskNote.Companion.Core` 순수 도메인 프로젝트 생성
3. 활동 분류·일일 상한·멱등 보상 정책 단위 테스트
