# deskNote 기반 Pet AI 업무 메모 스핀오프 구현 계획서

작성일: 2026-08-31  
기준 문서: `deskNote-petAI-spin-off-analysis.ko.md`  
기준 저장소: [`Ubermensch216/deskNote`](https://github.com/Ubermensch216/deskNote) (`main`, `fedebb0e0c3b0564c2b564e53467fa2e487b165f`)  
계획 성격: 제품·게임 시스템·기술 아키텍처·검증·GitHub 실행 백로그를 함께 다루는 구현 기준서

## 1. 실행 결론

이 프로젝트의 목표는 deskNote에 별도의 펫 미니게임을 얹는 것이 아니다. **업무 메모를 기록하고, 다시 찾고, 실제 행동으로 마무리하는 과정 자체가 반려 AI와의 관계 성장으로 읽히는 로컬 우선 업무 도구**를 만드는 것이다.

제품 문장은 다음으로 고정한다.

> 기록한 업무 맥락을 반려 AI가 기억하고, 필요한 순간에 근거와 함께 되돌려준다.

구현의 핵심 결정은 다음과 같다.

1. `deskNote`의 메모·저장·검색·알림·이력·로컬 AI를 제품의 본체로 유지한다.
2. 게임 상태는 AI가 아니라 **결정적 규칙 엔진**이 계산한다. AI는 표현과 제안을 맡고 점수·성장·보상 판정을 맡지 않는다.
3. 점수는 메모 수나 타이핑 양이 아니라 `기록 → 회상 → 해결`의 의미 있는 업무 행동에만 부여한다.
4. 펫은 생산성을 감시하거나 벌하지 않는다. 휴식일, 낮은 활동량, 연속 기록 중단으로 관계 수치가 감소하지 않는다.
5. MVP에는 가상 화폐, 상점, 리더보드, 가챠, 캐릭터 다종 수집, 사진 앨범을 넣지 않는다.
6. deskNote가 내세우는 완전 로컬·무계정·무구독 가치가 더 강한 차별점이므로, 외부 AI 제공자는 MVP 범위에서 제외한다.
7. 기존 사용자의 첫 실행에 컴패니언을 강제로 띄우지 않는다. 짧은 소개 후 사용자가 켜는 방식으로 배포한다.

예상 범위는 1인 개발 기준 **37~50 개발일**, 안정화와 소규모 베타를 포함해 약 **7~10주**다. 2인이 UI/도메인을 나누면 달력 기간을 5~7주로 줄일 수 있지만, 저장 경로와 게임 이벤트 계측은 한 명이 소유해야 한다.

## 2. 원 분석 문서에 대한 평가

### 2.1 그대로 채택할 판단

원 분석은 다음 판단에서 정확하다.

- Python/PySide6 런타임을 합치지 않고 C#/.NET으로 도메인만 포팅한다.
- 두 번째 DB를 만들지 않고 deskNote의 SQLite 수명주기와 백업 경계를 유지한다.
- 펫의 밥·청결 감쇠를 업무 앱에 가져오지 않는다.
- 선제 대화에는 시간당 상한, 동일 트리거 쿨다운, 최근 상호작용 억제, 조용한 시간이 필요하다.
- AI 액션은 `제안 → 근거 확인 → 사용자 승인 → 실행` 순서를 지킨다.
- 메모 본문은 관계 이벤트에 복제하지 않고 기존 노트가 단일 원본이어야 한다.
- 사진 처리, 90개 업적, 보상 상점, 7종 수집은 핵심 루프 검증 뒤로 미룬다.
- `NoteWindow.xaml.cs`, `NoteWindowManager.cs`, `App.xaml.cs`의 책임 분리가 컴패니언 추가보다 먼저다.

### 2.2 구현 계획으로 보강해야 할 부분

원 분석은 좋은 방향 보고서지만, 바로 이슈로 옮기기에는 다음 항목이 부족하다.

1. **중복 구현 방지**: 오늘 브리핑, 관련 메모, 하이브리드 검색, 출처 표시, AI 적용 승인은 이미 구현돼 있다. 새 기능으로 다시 만들 것이 아니라 컴패니언이 이 기능을 호출하고 결과를 표현하게 해야 한다.
2. **보상 가능한 행동의 정확한 정의**: 단순 저장, 반복 열기, 체크박스 토글을 그대로 점수화하면 메모 스팸과 체크박스 왕복으로 성장이 조작된다.
3. **게임 밸런스**: 하루 상한, 중복 방지, 품질 신호, 휴식일 처리, 보상 속도, 장기 성장 곡선이 필요하다.
4. **UI 존재 방식**: 모든 메모 창에 펫을 붙이면 시각적 잡음과 메모 입력 지연이 생긴다. 화면 전체에 하나의 컴패니언만 존재해야 한다.
5. **아키텍처 접점**: 현재 저장·검색·알림 흐름에서 어떤 이벤트를 어디서 발행하고 어떻게 중복 없이 투영할지 정해야 한다.
6. **기능 플래그와 롤백**: 기존 사용자의 DB를 안전하게 올리고, 컴패니언을 끈 상태에서 이전 deskNote처럼 동작하게 해야 한다.
7. **검증 수치**: “재미있다”가 아니라 회상 유용성, 방해율, 제안 수락률, 메모 입력 성능으로 성공을 판정해야 한다.
8. **접근성·업무 환경 규칙**: 모션 줄이기, 전체 화면 앱 감지, 화면 공유, 조용한 시간, 키보드 접근이 필요하다.

### 2.3 GitHub 실물 대조에서 추가로 확인한 위험

GitHub의 현재 `main`은 원 분석의 기준 커밋과 동일하고, 트리는 187개 파일이다. 따라서 원 분석은 시점상 낡지 않았다. 다만 다음 사항을 계획에 추가해야 한다.

- `.github/workflows`가 없어 테스트·빌드·패키징이 자동 실행되지 않는다.
- [`assets/icon/__pycache__/build_icon.cpython-314.pyc`](https://github.com/Ubermensch216/deskNote/blob/main/assets/icon/__pycache__/build_icon.cpython-314.pyc)가 추적 중이다. deskNote 쪽에도 생성물이 들어가 있으므로 정리 대상은 `pet_AI_v2`만이 아니다.
- [`docs/development.md`](https://github.com/Ubermensch216/deskNote/blob/main/docs/development.md)는 504개 테스트 통과라고 적지만 원 분석은 정적 정의 356개라고 적는다. 측정 방식이 다르므로 CI의 실행 결과를 유일한 기준으로 만들어야 한다.
- 최신 커밋 `fedebb0e`는 문서·AI·앱·UI·에셋을 포함한 약 50개 파일을 한 번에 건드렸고 메시지가 `ㄴ`이다. 기능별 회귀와 변경 이유를 추적하기 어렵다.
- [`NoteWindow.xaml.cs`](https://github.com/Ubermensch216/deskNote/blob/main/src/DeskNote.App/Views/NoteWindow.xaml.cs)는 1,674줄, [`NoteWindowManager.cs`](https://github.com/Ubermensch216/deskNote/blob/main/src/DeskNote.App/Services/NoteWindowManager.cs)는 757줄이다. 컴패니언 동작을 이 둘에 직접 추가하지 않는다.
- [`ILocalAiService.cs`](https://github.com/Ubermensch216/deskNote/blob/main/src/DeskNote.Core/Abstractions/ILocalAiService.cs)의 설명은 명명 파이프 뒤 별도 worker를 전제로 하지만 실제 [`OllamaAiService.cs`](https://github.com/Ubermensch216/deskNote/blob/main/src/DeskNote.Ai/OllamaAiService.cs)는 Ollama에 직접 HTTP 호출한다. 안전 경계 문서를 실제 구현에 맞추거나 worker 격리를 구현해야 한다.
- 중앙 패키지 파일에는 DI 패키지가 정의돼 있지만 App 프로젝트는 참조하지 않고 `App.xaml.cs`가 수동으로 전체 객체 그래프를 조립한다.
- 라이선스 파일이 없다. 기존 코드와 새 캐릭터 에셋을 외부 공개하기 전에 소유권과 배포 조건을 확정해야 한다.

## 3. 제품 정의

### 3.1 핵심 사용자

MVP는 다음 한 유형을 우선한다.

> Windows에서 메모를 빠르게 쌓지만, 과거 메모를 제때 다시 찾거나 미완료 업무를 정리하는 데 어려움을 느끼는 개인 지식 노동자.

팀 경쟁, 관리자 대시보드, 직원 생산성 비교는 범위 밖이다. 업무용 게임화가 감시 도구로 읽히는 순간 컴패니언의 관계성은 무너진다.

### 3.2 사용자가 얻는 가치

- 기록할 때: 기존 deskNote와 같은 속도로 생각을 붙잡는다.
- 다시 볼 때: 과거 메모가 왜 지금 관련 있는지 출처와 함께 확인한다.
- 마칠 때: 체크리스트·알림·AI 제안을 사용자가 승인하고 완료한다.
- 돌아볼 때: 펫의 성장이 “앱을 오래 켰다”가 아니라 “기억을 실제 업무에 다시 썼다”는 기록이 된다.

### 3.3 북극성 지표

**주간 유용 회상 수(Weekly Useful Recalls)**를 북극성 지표로 둔다.

유용 회상은 다음 조건 중 하나를 만족한 과거 메모 재방문이다.

- 검색, 관련 메모, 브리핑, 컴패니언 제안을 통해 메모를 열고 8초 이상 확인했다.
- 연 뒤 체크리스트를 완료하거나 알림을 처리했다.
- 연 뒤 본문을 의미 있게 수정했다.
- 그 메모를 근거로 한 AI 제안을 승인했다.

단순히 같은 메모를 닫고 다시 여는 것은 회상으로 세지 않는다.

## 4. 게임 디자인

### 4.1 네 겹의 루프

| 루프 | 사용자 행동 | 컴패니언 반응 | 제품 가치 |
|---|---|---|---|
| 순간 | 의미 있는 메모 기록·체크 완료 | 1~2초의 작은 표정·빛 | 즉각적인 긍정 피드백 |
| 세션 | 검색으로 과거 메모 회상, 관련 메모 연결 | “기억을 찾았어요” 카드 | 메모 재사용 촉진 |
| 하루 | 기록·회상·해결 중 선택한 1~3개 리추얼 | 하루 기록과 짧은 회고 | 습관 형성 |
| 주간 | 한 주의 유용 회상과 해결을 돌아봄 | 성장 장면·기억 카드 | 장기 관계와 자기 효능감 |

모든 루프는 컴패니언을 꺼도 원래 업무 동작이 완성돼야 한다.

> **2026-09-01 개정 안내**: 아래 4.2~4.5의 성장 규칙은 베타(V1) 기준이며 더 이상 유효하지 않다.
> 하루 최대 성장이 29점에 그쳐 펫이 앱 사용 보상에 머문다는 판단으로, 하루 100점을 앱 30점 · 돌봄
> 70점으로 나누는 성장 규칙 V2로 개편했다. 세 축은 성장을 결정하지 않는 평생 성향 카운터가 되었고,
> 선택형 일일 리추얼은 날짜에서 파생되는 오늘의 미션으로 대체되었다. 현행 규칙은
> [docs/companion.md](docs/companion.md)를 따른다.

### 4.2 성장 축

단일 경험치 대신 세 축을 사용한다.

- **호기심(Curiosity)**: 새로운 맥락을 의미 있게 기록함
- **통찰(Insight)**: 과거 메모를 검색·관련 메모·브리핑으로 다시 활용함
- **신뢰(Reliability)**: 체크리스트·알림·승인형 제안을 마무리함

세 축은 사용자가 어떤 방식으로 앱의 도움을 받는지 보여 주는 설명형 지표다. 어느 축이 낮아도 실패 상태를 만들지 않는다.

### 4.3 MVP 보상 규칙

| 이벤트 | 기본 성장 | 1일 인정 상한 | 중복 방지 조건 |
|---|---:|---:|---|
| 의미 있는 기록 | 호기심 +1 | 5회 | 빈 메모가 처음 20자 이상이 되거나 기존 메모에 40자 이상 의미 있는 변화 |
| 유용한 회상 | 통찰 +2 | 4회 | 검색/관련/브리핑/제안 경유 + 8초 체류 또는 후속 행동 |
| 체크리스트 완료 | 신뢰 +1 | 5회 | `미완료 → 완료` 전이만 인정, 같은 항목은 평생 1회 |
| 알림 처리 | 신뢰 +2 | 3회 | 완료 처리된 고유 reminder ID |
| AI 제안 승인 | 통찰 +1, 신뢰 +1 | 2회 | 고유 suggestion ID, 취소/재승인 중복 금지 |
| 오늘 브리핑 확인 | 통찰 +1 | 1회 | 브리핑 생성 후 근거 메모 하나 이상 열기 |

수치는 첫 베타를 위한 초기값이며 데이터가 아니라 가설이다. 밸런스 값은 코드에 흩뿌리지 않고 `CompanionBalanceV1` 한 곳에 둔다.

### 4.4 레벨과 성장 속도

- 각 축은 0~100의 내부 포인트를 갖고, UI에는 5단계 이름으로만 보여 준다.
- 단계 임계치는 `0 / 10 / 25 / 50 / 80`으로 시작한다.
- 세 축의 합이 15, 45, 90, 150에 도달할 때 외형 변화 또는 새 idle 표현을 하나 해금한다.
- MVP는 4단계 외형, 표정 6종 이내로 제한한다.
- 일일 최대 성장량을 제한해 메모를 대량 생성하는 플레이가 장기 성장을 압축하지 못하게 한다.
- 포인트는 감소하지 않는다. 밸런스 버전이 바뀌어도 이미 얻은 단계는 회수하지 않는다.

### 4.5 일일 리추얼

매일 강제 퀘스트 세 개를 주지 않는다. 앱 상태에서 가능한 다음 행동을 최대 세 개 보여 주고 사용자가 하나를 고른다.

예시:

- 오늘 새 메모 하나 남기기
- 지난주 메모 하나 다시 보기
- 미완료 체크 하나 정리하기

완료하지 않아도 아무 일도 일어나지 않는다. 자정에 실패 표시를 만들지 않고 “오늘은 쉬었어요”로 닫는다. 연속 기록은 경쟁 숫자 대신 최근 14일의 점 배열로 표현해 끊어진 하루를 손실로 느끼지 않게 한다.

### 4.6 선제 제안 규칙

MVP 트리거는 네 개만 허용한다.

1. 30분 안에 도래하는 알림
2. 현재 메모와 유사도가 충분히 높은 과거 메모
3. 7일 이상 열리지 않은 미완료 체크가 있는 메모
4. 사용자가 요청한 오늘 브리핑의 후속 근거

가드 조건은 모두 통과해야 한다.

- 시간당 최대 2회
- 하루 최대 5회
- 동일 트리거·동일 note ID는 24시간 쿨다운
- 사용자 상호작용 후 10분 억제
- 기본 조용한 시간 20:00~09:00
- 전체 화면 앱, 화면 공유 가능성이 높은 상태, 프레젠테이션 모드에서는 억제
- 3회 연속 무시된 트리거는 7일 동안 자동 음소거

제안은 토스트가 아니라 컴패니언 옆의 작은 카드로 나타나며, 포커스를 빼앗지 않는다.

### 4.7 절대 넣지 않을 패턴

- 접속하지 않으면 아프거나 관계가 떨어지는 펫
- 빨간 숫자 배지로 미완료를 압박하는 UI
- 무한 연속 기록과 연속 기록 복구권 판매
- 확률형 보상, 가상 화폐, 일일 상점
- 동료 순위, 팀 생산성 비교, 관리자용 행동 로그
- 원문 내용이나 민감한 태그가 캐릭터 말풍선에 예고 없이 노출되는 방식
- 소리 자동 재생, 흔들리는 상시 애니메이션, 화면 위를 가로지르는 이동

## 5. MVP 사용자 경험

### 5.1 컴패니언의 위치

컴패니언은 메모마다 하나씩 붙이지 않고 **프로세스 전체에 하나만** 둔다.

- 기본 위치: 화면 오른쪽 아래 작업 영역, 작업표시줄 위
- 기본 상태: 일반 창보다 위에 두지 않음
- 선택 상태: “항상 보이기”를 사용자가 켤 수 있음
- 드래그 위치와 모니터를 저장함
- 클릭: 오늘 상태와 제안 카드 열기
- 우클릭: 잠시 숨기기, 조용한 시간, 모션 줄이기, 끄기
- 입력 중: idle 모션만 사용하고 말풍선을 띄우지 않음

기존 스티키 메모의 “쉬고 있는 종이에는 조작 요소가 없다”는 원칙을 침범하지 않도록 `NoteWindow` 안에 캐릭터를 넣지 않는다.

### 5.2 표현 상태

MVP 상태는 다음 6개로 제한한다.

- Idle
- Noticed
- Thinking
- RecallFound
- Completed
- Resting

표현은 1~2초 안에 끝나고, 실행 중인 모션을 다시 큐에 쌓지 않는다. Windows의 모션 줄이기 설정 또는 앱 설정이 켜지면 정적 이미지와 색 변화로 대체한다.

### 5.3 카드 유형

모든 카드는 닫기와 음소거를 제공한다.

| 카드 | 버튼 | 자동 변경 여부 |
|---|---|---|
| 관련 기억 | 메모 열기 / 오늘 숨기기 | 없음 |
| 임박 알림 | 메모 열기 / 완료 / 미루기 | 클릭 후만 변경 |
| 오래된 미완료 | 메모 열기 / 이번 주 숨기기 | 없음 |
| 오늘 브리핑 | 브리핑 만들기 | 클릭 후 기존 `DailyBriefing` 호출 |
| 성장 기록 | 자세히 보기 | 없음 |

### 5.4 온보딩

기존 사용자에게는 업데이트 후 한 번만 다음을 제시한다.

1. 컴패니언이 하는 일: 기록 감시가 아니라 과거 메모 회상 지원
2. 데이터 위치: 기존 로컬 DB 안에 최소 메타데이터만 추가
3. 하지 않는 일: 외부 전송, 자동 편집, 생산성 감소 처벌
4. 선택: 지금 켜기 / 나중에 / 사용하지 않기

캐릭터 이름과 색은 선택할 수 있지만 종 선택과 성격 질문지는 MVP에서 생략한다.

## 6. 기술 아키텍처

### 6.1 목표 구조

```text
DeskNote.App (WinUI 3)
 ├─ Notes UI
 ├─ CompanionWindow / CompanionPanel / SuggestionCard
 ├─ CompositionRoot
 └─ feature-scoped coordinators

DeskNote.Core
 ├─ 기존 노트 모델·계약·정책
 └─ NoteActivity 계약(업무 행동을 표현하는 최소 이벤트)

DeskNote.Companion.Core (신규, net10.0)
 ├─ CompanionProfile / GrowthState / DailyRitual
 ├─ ActivityClassifier / RewardPolicy / ProactivePolicy
 ├─ CompanionEvent / TypedSuggestion
 └─ UI·DB·AI 비의존 결정적 엔진

DeskNote.Data
 ├─ 기존 SQLite/FTS/vector
 ├─ Companion repositories
 └─ migration 003+

DeskNote.Ai
 ├─ 기존 Ollama 기능
 └─ 선택형 CompanionNarrator(문장 표현만 담당)
```

`DeskNote.Companion.Core`를 별도 프로젝트로 두는 이유는 게임 규칙이 `NoteWindow`나 AI 프롬프트에 새어 나가는 것을 막고, 시계와 난수 시드를 주입해 전부 단위 테스트할 수 있게 하기 위해서다.

### 6.2 조립 루트 분리

`App.xaml.cs`에서는 다음만 남긴다.

- 예외 처리 등록
- `CompositionRoot.BuildAsync()` 호출
- `ApplicationLifetime.StartAsync()` 호출
- 종료 시 `StopAsync()` 호출

객체 생성은 `CompositionRoot`로 이동하고, 장기 실행 서비스는 명시적인 `StartAsync/StopAsync` 수명주기를 갖는다. DI 컨테이너 도입 자체가 목적은 아니며, 현재 중앙 패키지에 있는 `Microsoft.Extensions.DependencyInjection`을 실제로 사용할지 수동 팩터리를 유지할지는 Phase 1에서 한 번 결정한다.

### 6.3 이벤트 계측

저장 한 번을 곧바로 성장 이벤트로 취급하지 않는다. 다음 흐름을 사용한다.

```text
사용자 동작
  → 기존 노트/알림 동작 성공
  → NoteActivityCandidate 생성
  → ActivityClassifier가 의미·중복·상한 판정
  → companion_event_ledger에 멱등 기록
  → GrowthProjector가 일일/누적 상태 갱신
  → UI는 투영 결과만 구독
```

핵심 규칙:

- `source_event_id + rule_version`에 유일 제약을 둬 재시도 시 중복 보상을 막는다.
- 자동 저장 300ms 주기마다 이벤트를 만들지 않는다. 편집 세션의 시작/종료와 before/after 차이로 의미 있는 기록을 판정한다.
- 체크리스트는 안정적인 item ID가 필요하다. 현재 본문에서 매번 투영될 때 ID가 재생성된다면 `note_id + normalized text + first_seen_at` 기반의 별도 completion key를 사용하거나 투영 안정화가 선행돼야 한다.
- 검색 결과 클릭에는 `entry_point=search|related|briefing|companion`을 붙인다.
- 창을 연 사실만으로 회상 완료를 기록하지 않고 체류 시간 또는 후속 행동을 기다린다.
- 게임 이벤트 저장 실패가 노트 저장 실패로 전파되지 않는다. 메모 데이터가 항상 우선이다.

### 6.4 데이터 모델

`003_companion.sql`에 다음 테이블을 추가한다.

```sql
CREATE TABLE companion_profiles (
    id               TEXT NOT NULL PRIMARY KEY,
    name             TEXT NOT NULL,
    appearance_key   TEXT NOT NULL,
    created_at       TEXT NOT NULL,
    enabled          INTEGER NOT NULL DEFAULT 0,
    rule_version     INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE companion_event_ledger (
    id               TEXT NOT NULL PRIMARY KEY,
    companion_id     TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    source_event_id  TEXT NOT NULL,
    event_type       INTEGER NOT NULL,
    note_id          TEXT NULL REFERENCES notes(id) ON DELETE SET NULL,
    source_entity_id TEXT NULL,
    curiosity_delta  INTEGER NOT NULL DEFAULT 0,
    insight_delta    INTEGER NOT NULL DEFAULT 0,
    reliability_delta INTEGER NOT NULL DEFAULT 0,
    occurred_at      TEXT NOT NULL,
    local_date       TEXT NOT NULL,
    rule_version     INTEGER NOT NULL,
    payload_json     TEXT NULL,
    UNIQUE(source_event_id, rule_version)
);

CREATE INDEX idx_companion_events_date
ON companion_event_ledger(companion_id, local_date, event_type);

CREATE TABLE companion_daily_progress (
    companion_id     TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    local_date       TEXT NOT NULL,
    capture_count    INTEGER NOT NULL DEFAULT 0,
    recall_count     INTEGER NOT NULL DEFAULT 0,
    resolve_count    INTEGER NOT NULL DEFAULT 0,
    chosen_ritual    INTEGER NULL,
    ritual_completed INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(companion_id, local_date)
);

CREATE TABLE companion_suggestions (
    id               TEXT NOT NULL PRIMARY KEY,
    companion_id     TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    suggestion_type  INTEGER NOT NULL,
    source_note_id   TEXT NULL REFERENCES notes(id) ON DELETE CASCADE,
    dedupe_key       TEXT NOT NULL,
    status           INTEGER NOT NULL,
    created_at       TEXT NOT NULL,
    expires_at       TEXT NOT NULL,
    acted_at         TEXT NULL,
    payload_json     TEXT NOT NULL,
    UNIQUE(companion_id, dedupe_key)
);

CREATE TABLE companion_preferences (
    companion_id     TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    key              TEXT NOT NULL,
    value            TEXT NOT NULL,
    PRIMARY KEY(companion_id, key)
);
```

`payload_json`에는 본문, 제목 전문, AI 답변을 넣지 않는다. 트리거 유형, 점수 근거, UI 표시용 최소 값만 둔다. 카드 표시 시 현재 노트 저장소에서 제목과 미리보기를 다시 읽는다.

### 6.5 시간과 날짜

- 이벤트 원본 시각은 UTC로 저장한다.
- 일일 상한 계산을 위해 이벤트 발생 당시의 `local_date`를 함께 저장한다.
- 자정 경계, 시간대 변경, DST를 단위 테스트한다.
- 시스템 시간이 과거로 이동해도 이미 기록된 `source_event_id`가 다시 보상되지 않아야 한다.

### 6.6 AI 경계

MVP의 캐릭터 대사는 템플릿과 결정적 상태 조합으로 만든다. 예를 들어 `RecallFound + ko-KR + quiet` 키로 짧은 문장을 선택한다. 선택은 날짜와 companion ID에서 만든 고정 시드를 사용해 같은 순간 재실행 시 대사가 출렁이지 않게 한다.

AI를 사용하는 경우에도 역할은 다음으로 제한한다.

- 브리핑 문장 다듬기
- 사용자가 연 채팅에서 메모 근거 기반 답변
- 이미 결정된 제안의 한 줄 설명

AI가 할 수 없는 일:

- 성장 포인트 결정
- 감정 상태 판정
- 사용자 생산성 평가
- 알림·메모·체크리스트 자동 변경
- 선제 제안 발송 시점 결정

`CompanionNarrator`는 실패하면 즉시 템플릿으로 폴백한다. 컴패니언 표시가 25~60초의 로컬 모델 지연을 기다리지 않는다.

### 6.7 기존 기능 재사용 지도

| 필요한 기능 | 새로 만들지 않고 사용할 기존 구현 | 추가할 어댑터 |
|---|---|---|
| 오늘 브리핑 | `DailyBriefing` | `BriefingSuggestionHandler` |
| 관련 기억 | `NoteNeighbourhood`, `HybridRetriever` | `RecallCandidateProvider` |
| 메모 열기 | `NoteWindowManager.FocusAsync` | typed action handler |
| 알림 | `ReminderService`, `IReminderRepository` | due trigger + completion event |
| AI 근거 | `AiChatWindow`, scoped note IDs | companion entry point 표시 |
| 안전한 적용 | AI preview/proposal + `NoteSavePipeline` | 승인 완료 이벤트 |
| 설정 | `ISettingsStore` | 정식 Settings 화면과 companion namespace |

## 7. 단계별 구현 로드맵

### Phase 0 — 기준선과 저장소 위생 (2~3일)

작업:

- `deskNote`의 전체 이력을 보존해 새 비공개 저장소 `deskNote-petAI`를 만든다.
- 새 저장소의 `main`을 `fedebb0e`에 고정하고 `develop` 또는 짧은 기능 브랜치 전략을 선택한다.
- `.github/workflows/ci.yml`에 Windows 빌드, 전체 테스트, Release publish smoke test를 추가한다.
- `__pycache__`, `*.pyc`, 테스트·캡처 임시 산출물의 추적을 중단하고 `.gitignore`를 보강한다.
- 테스트 개수는 문서에 수기로 쓰지 않고 CI 결과 링크로 대체한다.
- `ILocalAiService`의 격리 설명을 실제 구현과 일치시킨다. worker 격리는 별도 보안 이슈로 분리한다.
- LICENSE, 캐릭터 에셋 소유권, 공개 여부를 결정한다.
- 브랜치 보호: CI 필수, squash merge, PR 1개당 한 기능, 제목 규칙을 설정한다.

완료 조건:

- 깨끗한 checkout에서 `dotnet build`, `dotnet test`, win-x64 publish가 자동 통과한다.
- CI가 실제 실행 테스트 수와 실패 테스트를 보여 준다.
- 빌드 후 Git 상태가 깨끗하다.
- 기존 성능 기준값을 결과물로 보관한다.

### Phase 1 — 컴패니언을 넣을 수 있는 접점 만들기 (5~7일)

작업:

- `App.xaml.cs`의 조립을 `CompositionRoot`와 `ApplicationLifetime`으로 분리한다.
- `NoteWindow.xaml.cs`에서 AI 동작, 첨부, 드래그, chrome, appearance를 기능별 partial/controller로 분리한다.
- `NoteWindowManager`에서 브리핑·관련 메모·AI 채팅 coordinator를 분리한다.
- `NoteSavePipeline.SaveAsync`가 변경 전후와 저장 결과를 표현하는 `NoteSaveResult`를 반환하게 한다.
- 검색/관련/브리핑/컴패니언 진입점을 `NoteOpenContext`로 전달한다.
- 체크리스트 완료의 안정적인 중복 방지 키 전략을 확정한다.
- 정식 Settings 창의 뼈대를 만들고 기존 `ai.*`, `ui.*`, `input.*` 설정도 이곳에서 다룬다.

완료 조건:

- 기존 UI와 단축키 동작이 동일하다.
- 컴패니언 코드를 넣지 않은 상태에서 기존 테스트와 성능 기준을 통과한다.
- 메모 저장, 검색 결과 열기, 체크 완료, 알림 처리에 typed activity 후보를 연결할 수 있다.

### Phase 2 — 순수 게임 도메인과 저장 (6~8일)

작업:

- `DeskNote.Companion.Core`와 테스트 프로젝트를 추가한다.
- `ActivityClassifier`, `RewardPolicy`, `DailyCapPolicy`, `GrowthProjector`, `ProactivePolicy`를 구현한다.
- migration 003과 companion repository를 구현한다.
- 이벤트 ledger의 멱등성, 일일 상한, 시간대, 재시작 복원을 테스트한다.
- `CompanionActivityQueue`를 추가해 노트 저장 후 비동기로 이벤트를 기록한다.
- 시작 시 처리되지 않은 후보를 재평가할 수 있는 작은 재시도 경계를 둔다.
- 기능 플래그 `companion.enabled=false`를 기본값으로 추가한다.

완료 조건:

- UI와 AI 없이 고정 입력으로 성장 결과가 완전히 재현된다.
- 같은 이벤트를 100번 전달해도 보상은 한 번만 기록된다.
- 컴패니언 DB 쓰기 실패가 노트 저장을 실패시키지 않는다.
- 기존 v2 DB를 복사해 migration 003 후 모든 기존 메모·FTS·벡터가 보존된다.

### Phase 3 — 컴패니언 UI와 접근성 (7~10일)

작업:

- 단일 `CompanionWindow`와 위치 저장을 구현한다.
- 6개 표현 상태와 모션 줄이기 대체 표현을 만든다.
- `CompanionPanel`에 세 성장 축, 오늘의 리추얼, 최근 성장 이유를 표시한다.
- 관련 기억·임박 알림·브리핑 카드와 typed action handler를 구현한다.
- 키보드 포커스 순서, 스크린 리더 이름, 고대비, 150%/200% 배율을 검증한다.
- 전체 화면·프레젠테이션 상태에서 자동 숨김 또는 선제 제안 억제를 적용한다.
- 설정에서 켜기/끄기, 항상 보이기, 조용한 시간, 모션 줄이기, 제안별 음소거를 제공한다.

완료 조건:

- 컴패니언은 메모 입력 포커스를 빼앗지 않는다.
- 컴패니언을 껐을 때 창·타이머·백그라운드 제안 생성이 모두 정지한다.
- idle 상태의 CPU와 메모리 증가가 정한 예산 안에 든다.
- 화면 배율과 다중 모니터 변경 뒤에도 작업 영역 안에 복원된다.

### Phase 4 — 회상 루프와 안전한 선제성 (7~9일)

작업:

- `RecallCandidateProvider`를 기존 관련 메모·하이브리드 검색 위에 구현한다.
- 4개 트리거와 ProactiveGuard를 연결한다.
- 유용 회상의 8초 체류/후속 행동 판정을 구현한다.
- 오늘 브리핑에서 근거 메모를 열면 통찰 이벤트를 기록한다.
- 연속 무시 트리거 자동 음소거와 카드별 “왜 이걸 보여주나요?” 설명을 추가한다.
- 로컬 템플릿 대사와 선택형 `CompanionNarrator`를 연결한다.

완료 조건:

- 모든 카드가 근거 note ID 또는 reminder ID를 가진다.
- 같은 제안은 재시작 후에도 중복 표시되지 않는다.
- 조용한 시간과 전체 화면에서 선제 카드가 0건이다.
- AI를 제거하거나 Ollama를 종료해도 카드·성장·리추얼이 동작한다.

### Phase 5 — 베타, 밸런싱, 배포 (7~10일)

작업:

- 5~8명 알파에서 방해 요소와 이해하기 어려운 성장 이유를 수정한다.
- 15~20명 비공개 베타를 2주 운영한다.
- 네트워크 전송 없는 로컬 진단 화면으로 제안 생성/노출/열기/무시 집계를 보여 준다.
- 사용자가 진단 데이터를 Markdown/JSON으로 직접 내보내 연구자에게 제공할 수 있게 한다.
- 성능, 강제 종료, 절전 복귀, DB 백업·복원, 업그레이드·구버전 롤백을 검증한다.
- 캐릭터 에셋 라이선스와 설치 패키지 서명 계획을 확정한다.

출시 게이트:

- 메모 생성/저장 p95가 기존 대비 10% 이상 악화되지 않고 기존 SLO를 유지한다.
- 컴패니언 비활성 시 기존 deskNote와 기능·성능 차이가 없다.
- 선제 제안에 “방해됐다” 응답이 10% 미만이다.
- 노출된 관련 기억 카드의 메모 열기 비율이 25% 이상이다.
- 열린 관련 기억 중 유용 회상 판정 비율이 40% 이상이다.
- 베타 사용자의 컴패니언 완전 비활성 비율이 25% 미만이다.
- 데이터 손실, 자동 편집, 조용한 시간 위반은 0건이다.

## 8. GitHub 이슈 단위 백로그

다음 표는 그대로 GitHub 이슈로 만들 수 있는 최소 단위다. 아직 사용자 승인을 받지 않은 외부 변경이므로 이 계획서 작성 단계에서는 이슈를 실제 생성하지 않는다.

| ID | 우선순위 | 이슈 | 주요 파일/영역 | 완료 기준 |
|---|---|---|---|---|
| HYG-01 | P0 | Windows CI 추가 | `.github/workflows/ci.yml` | build/test/publish 필수 체크 |
| HYG-02 | P0 | 추적 생성물 제거와 ignore 보강 | `.gitignore`, `assets/icon` | 빌드 후 clean |
| HYG-03 | P0 | 테스트 수치·AI 격리 문서 정합화 | `docs/*`, `ILocalAiService.cs` | 코드와 문서 불일치 제거 |
| ARC-01 | P0 | CompositionRoot 분리 | `App.xaml.cs`, `Services/CompositionRoot.cs` | App가 수명주기만 소유 |
| ARC-02 | P0 | NoteWindow 책임 분할 | `Views/NoteWindow*` | 각 기능 경계 테스트 가능 |
| ARC-03 | P0 | NoteOpenContext 도입 | 검색·관련·브리핑·manager | 진입점 계측 가능 |
| EVT-01 | P0 | NoteActivity 계약과 분류기 | Core, Companion.Core | 의미 있는 행동만 분류 |
| EVT-02 | P0 | checklist completion 안정 키 | parser/repository/companion | 토글 왕복 중복 보상 없음 |
| DB-01 | P0 | companion migration 003 | Data/Migrations | v2→v3 무손실 |
| DB-02 | P0 | event ledger와 projector | Data, Companion.Core | 멱등·상한·재시작 보장 |
| CMP-01 | P1 | CompanionWindow | App/Views | 단일 인스턴스·위치 복원 |
| CMP-02 | P1 | 6개 상태 표현과 reduced motion | App/Assets, App/Theming | 접근성 모드 제공 |
| CMP-03 | P1 | 성장 패널과 오늘 리추얼 | App/Views | 성장 이유 설명 가능 |
| PRO-01 | P1 | ProactivePolicy | Companion.Core | 모든 빈도 제한 테스트 |
| PRO-02 | P1 | 관련 기억 카드 | NoteNeighbourhood adapter | 근거·열기·음소거 |
| PRO-03 | P1 | 알림/미완료 카드 | Reminder/checklist adapter | 승인형 액션만 실행 |
| SET-01 | P1 | 정식 Settings 화면 | App/Views/Settings | AI/컴패니언/입력 설정 통합 |
| A11Y-01 | P1 | 키보드·스크린리더·고대비 | 전체 신규 UI | 자동/수동 접근성 체크 |
| PERF-01 | P0 | 성능 회귀 벤치마크 확장 | Benchmarks | 저장·시작·idle 예산 확인 |
| REL-01 | P0 | migration/rollback/backup 시나리오 | Data.Tests, docs | 구버전이 신규 테이블을 무시 |
| BETA-01 | P1 | 로컬 진단 및 내보내기 | App, Data | 외부 전송 없이 측정 가능 |

## 9. 테스트 전략

### 9.1 Companion.Core 단위 테스트

- 같은 입력·시계·규칙 버전에서 같은 결과
- 하루 상한 도달 전후 경계
- 빈 메모, 공백 변경, 서식만 변경, 대량 붙여넣기
- 체크리스트 완료→해제→재완료 중복 방지
- 시간대 변경과 자정
- 휴식일에 포인트 감소 없음
- 동일 트리거 쿨다운
- 최근 상호작용/조용한 시간/전체 화면 억제
- 3회 연속 무시 후 7일 음소거
- 규칙 버전 변경 시 기존 보상 보존

### 9.2 데이터 테스트

- migration 002 상태 DB를 fixture로 두고 003 적용
- 기존 note/FTS/embedding 행과 foreign key 검증
- ledger unique 제약과 동시 삽입
- note purge 시 `note_id` 처리 정책 검증
- profile 전체 삭제 시 companion 데이터 완전 삭제
- 백업 복원 뒤 성장 투영 동일성

### 9.3 통합 테스트

- 기록 → 저장 → 이벤트 → 성장 → UI 상태
- 검색 → 메모 열기 → 8초 → 회상 인정
- 관련 카드 → 열기 → 체크 완료 → 두 축 성장
- AI 제안 → 승인 → revision 보존 → 보상 1회
- Ollama 미설치/중단 상태의 전체 흐름
- 강제 종료 후 ledger 중복 없이 복구
- companion off에서 네트워크·타이머·DB 쓰기 없음

### 9.4 UI와 접근성 테스트

- 100%/150%/200% 배율, 다중 모니터, 모니터 제거
- 작업표시줄 위치 변경
- 전체 화면 앱 위 미노출
- 키보드만으로 카드 열기·닫기·음소거
- 스크린리더가 성장 수치보다 의미를 읽음
- 고대비와 모션 줄이기
- 240×180 메모 입력 중 포커스 탈취 없음

### 9.5 성능 예산

| 항목 | 예산 |
|---|---:|
| 기존 note save p95 | 기존 <30ms SLO 유지, 기준 대비 +10% 이내 |
| activity 분류 | UI thread 동기 구간 <1ms |
| ledger 기록 | 저장 완료 뒤 비동기, p95 <10ms |
| 앱 시작 | 컴패니언 때문에 첫 메모 표시 지연 0ms 목표 |
| companion idle CPU | 추가 평균 0.10% 이하 |
| companion 메모리 | 추가 working set 35MB 이하 |
| 카드 생성 | 모델 없이 200ms 이내 |
| 애니메이션 | 입력/드래그 중 프레임 드롭 체감 없음 |

## 10. 프라이버시·보안·업무 환경 원칙

1. 컴패니언 데이터는 기존 `%LOCALAPPDATA%\DeskNote\notes.db` 안에 둔다.
2. 성장 ledger는 본문을 저장하지 않는다.
3. 외부 텔레메트리를 기본 탑재하지 않는다.
4. 사용성 측정은 로컬 집계 화면과 사용자의 명시적 내보내기로 한다.
5. 캐릭터 말풍선에는 민감한 메모 제목을 기본 노출하지 않는다. “관련 메모가 있어요”를 먼저 보여 주고 카드 확장 시 제목을 표시한다.
6. AI 프롬프트의 메모 내용은 기존 `UNTRUSTED_NOTE_CONTEXT` 경계를 그대로 사용한다.
7. 컴패니언은 사용자 승인 없이 메모, 알림, 체크리스트, 노트북 소속을 바꾸지 않는다.
8. 전체 삭제는 테이블 수동 열거가 아니라 companion profile cascade와 데이터 소유권 테스트로 검증한다.
9. 화면 공유·프레젠테이션 상황에서는 선제 노출을 억제한다.
10. 회사 환경을 고려해 사운드는 MVP에서 제공하지 않는다.

## 11. 배포와 롤백

### 11.1 기능 플래그

초기 플래그:

- `companion.enabled`
- `companion.proactive.enabled`
- `companion.alwaysVisible`
- `companion.reduceMotion`
- `companion.quietHours.start`
- `companion.quietHours.end`
- `companion.ruleVersion`

`companion.enabled=false`일 때 신규 UI를 만들지 않고, proactive timer를 시작하지 않고, 활동 이벤트를 ledger에 쓰지 않는다.

### 11.2 DB 롤백

migration 003은 기존 테이블을 변경하지 않는 additive migration으로 만든다. 구버전 deskNote는 신규 테이블을 무시할 수 있어야 한다. down migration은 사용자 데이터를 지울 위험이 있으므로 제공하지 않는다.

문제가 생기면 다음 순서로 롤백한다.

1. 설정에서 companion 기능 비활성
2. 이전 실행 파일로 교체
3. 기존 메모 기능 정상 확인
4. companion 테이블은 보존
5. 수정 버전에서 같은 rule version과 ledger를 이어서 사용

### 11.3 단계적 공개

- Canary: 개발자 데이터 복제본, 3일
- Alpha: 5~8명, 1주
- Private beta: 15~20명, 2주
- Release candidate: 신규 설치와 기존 v2 DB 업그레이드 각각 검증
- 일반 배포: 컴패니언은 opt-in, 2개 릴리스 뒤 기본 제안 여부 재검토

## 12. 범위 관리

### MVP에 포함

- 캐릭터 1종, 외형 성장 4단계, 표현 6종
- 세 성장 축과 일일 상한
- 선택형 일일 리추얼 1개
- 관련 기억·임박 알림·오래된 미완료·브리핑 카드
- 조용한 시간, 빈도 제한, 음소거, 모션 줄이기
- 성장 이유 기록
- 완전 로컬 동작과 AI 없는 폴백

### 베타 성공 후 고려

- 성격 3종
- 주간 기억 타임라인
- 사용자가 선택한 실제 반려동물 사진 기반 외형
- 로컬 암호화 동기화
- 작은 업적 세트(최대 12개)
- 확장형 provider/skill 시스템

### 명시적 비범위

- Python/PySide6 런타임
- 두 번째 DB
- 외부 AI 제공자
- 팀/관리자 대시보드
- 리더보드와 소셜 경쟁
- 가상 화폐·상점·확률형 보상
- 사진 자동 수집·앨범
- 7종 캐릭터와 대규모 스프라이트 세트
- 관계 감소·죽음·질병·배고픔

## 13. 최종 의사결정 체크리스트

구현 시작 전에 다음만 확정하면 된다.

- [ ] 새 저장소 이름과 제품명
- [ ] 비공개 유지 기간과 LICENSE
- [ ] 캐릭터 에셋 제작 방식 및 소유권
- [ ] 체크리스트 안정 키 전략
- [ ] 화면 공유/전체 화면 감지의 기술 범위
- [ ] DI 컨테이너 도입 여부
- [ ] 알파 사용자 5~8명 확보

나머지는 이 계획의 기본값으로 진행할 수 있다.

## 14. 최종 권고

첫 릴리스의 성공 조건은 펫이 귀여운지가 아니다. 다음 세 가지가 동시에 성립해야 한다.

1. 사용자는 기존 deskNote와 같은 속도로 메모한다.
2. 컴패니언이 보여 준 과거 메모가 실제 업무에 다시 쓰인다.
3. 사용자는 방해받거나 평가받는 느낌 없이 관계가 쌓인다고 느낀다.

따라서 첫 개발 순서는 캐릭터 애니메이션이 아니라 **CI와 책임 분리 → 의미 있는 행동 이벤트 → 멱등 성장 규칙 → 단일 컴패니언 UI → 안전한 선제 제안**이다. 이 순서를 지키면 게임 요소는 업무를 덮는 장식이 아니라, deskNote의 가장 강한 기능인 기억 회수를 지속하게 만드는 피드백 시스템이 된다.
