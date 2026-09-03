# Phase 0 기준선

> 이 문서는 2026-08-31 시점의 **기록**이다. 이후 변경은 되돌아보기 위한 기준선으로만 쓰고, 현재 동작은 [architecture.md](architecture.md)를 본다.

기준 커밋: `fedebb0e0c3b0564c2b564e53467fa2e487b165f`  
측정일: 2026-08-31  
환경: Windows 10, win-x64, .NET SDK 10.0.400

## 가져온 상태

- Git 트리 파일 187개
- `src` 115개 파일, `tests` 44개 파일
- GitHub Actions 없음
- `assets/icon/__pycache__/build_icon.cpython-314.pyc` 추적 중
- 최신 커밋이 약 50개 파일을 변경했으나 메시지는 `ㄴ`

## 변경 전 테스트

`dotnet test --configuration Release` 결과:

- 전체 504
- 성공 503
- 실패 1
- 건너뜀 0

실패는 `AiTextCleanupTests.A_whole_proposal_reads_as_plain_note_text`의 기대 문자열이 Windows
체크아웃에서 CRLF가 된 반면 제품 코드는 의도대로 LF를 반환해 발생했다. 기대 문자열도 제품의
`NoteContent.NormalizeLineEndings`를 거치게 해 플랫폼 독립적으로 수정한다.

## Phase 0 검증 목표

- 전체 테스트 통과
- win-x64 Release 단일 파일 publish 성공
- 빌드·테스트·publish를 수행하는 Windows CI 추가
- 생성물 추적 제거와 ignore 규칙 보강
- AI 프로세스 격리 설명을 실제 Ollama HTTP 구현과 일치시킴

## Phase 0 완료 결과

- 전체 테스트 504/504 통과
- win-x64 Release 단일 파일 생성 성공(당시 실제 시작 검사는 누락)
- `DeskNote.exe` 230,941,441 bytes 생성 확인
- 2026-09-01 실제 시작 실패를 확인해 폴더형 self-contained ZIP과 시작 검사로 보정
- Windows GitHub Actions workflow 추가
- 기존 `RevisionPolicy` 서식 오류를 수정하고 CI 서식 검증 추가
- Python 캐시 추적 제거 및 ignore 규칙 추가
- `.gitattributes`로 소스·문서 줄바꿈을 LF로 고정
- 테스트 수의 수기 기준 제거
- `ILocalAiService` 설명을 현재 Ollama 별도 프로세스 + 로컬 HTTP 경계에 맞게 수정
