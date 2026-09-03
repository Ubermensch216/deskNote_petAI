# 개발

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

xunit.v3 는 Microsoft.Testing.Platform 러너를 직접 호스팅하고 .NET 10 SDK 는 VSTest 로 그것을 실행하지
않는다. 그래서 `global.json` 의 `test.runner` 로 러너를 지정해 둔다 — 이 항목이 없으면 `dotnet test` 는
바로 오류로 끝난다. 테스트 수는 기능과 이론 데이터에 따라 계속 달라지므로 문서에 고정하지 않는다.
현재 통과 여부와 실행된 테스트 수의 기준은 GitHub Actions의 `Build, test, and package` 실행 결과다.

## CI

`main` 과 pull request는 Windows에서 복원·서식 검증·Release 빌드·테스트·win-x64 배포 패키지 생성을
순서대로 실행한다. 마지막 단계는 배포된 `DeskNote.exe`를 8초 동안 직접 실행해 시작 중 비정상 종료도
검출한다.

CI가 실패한 변경은 병합하지 않는다. 특히 테스트 성공 뒤 publish까지 실행하는 이유는 WinUI 앱이
`dotnet run`에서는 정상이어도 publish 출력에서 리소스 인덱스를 잃을 수 있기 때문이다.
테스트 뒤에는 10,000개 메모 성능 게이트도 실행해 저장·검색·복원 경로의 회귀를 막는다.

## 배포 패키지 만들기

배포물은 self-contained 폴더를 담은 ZIP이다. .NET 런타임과 Windows App SDK 파일이 모두 포함되므로
받는 쪽은 별도 런타임을 설치하지 않는다. 다만 압축을 푼 뒤 **폴더 전체를 함께 보관**해야 하며,
`DeskNote.exe`만 다른 위치로 옮기면 안 된다.

```powershell
./scripts/publish-win-x64.ps1
```

스크립트는 `dist/DeskNote-win-x64/DeskNote.exe`를 포함한 폴더를 만들고, 실제 시작 검사를 통과한 뒤
`dist/DeskNote-win-x64.zip`으로 압축한다. CI도 같은 스크립트를 사용한다.

받는 쪽에 필요한 것은 Windows 10 1809 이상(x64)과 Visual C++ 재배포 가능 패키지뿐이다. 후자는
self-contained Windows App SDK가 요구하는 유일한 시스템 구성 요소이고 publish 출력에 들어가지 않는다.

### 단일 exe를 사용하지 않는 이유

.NET 10에서 Windows App SDK 네이티브 파일을 하나의 exe에 묶으면, 일부 환경에서 WinUI 활성화가
시작되기 전에 `0x80040111 (ClassFactory cannot supply requested class)`로 종료된다. Microsoft가 추적
중인 [WindowsAppSDK #6058](https://github.com/microsoft/WindowsAppSDK/issues/6058)과 같은 증상이다.
폴더형 self-contained 배포는 같은 바이너리와 런타임을 사용하면서도 이 경로를 거치지 않으며, 실제
실행 검사를 통과했다. 해당 문제가 해결되고 지원 조합에서 재검증되기 전까지 단일 exe는 만들지 않는다.

ARM64 프로젝트 빌드는 지원하지만 배포 패키지와 시작 검사는 아직 win-x64만 자동화한다. ARM64 배포를
추가할 때는 별도 하드웨어에서 같은 시작 검사를 통과시킨 뒤 전용 ZIP으로 제공한다.

> **`EnableMsixTooling` 은 켜 두어야 한다.** 이 속성이 꺼져 있으면 publish 출력에서 `DeskNote.pri` 가
> 조용히 빠지고, 앱은 빌드도 실행도 되다가 **창을 만드는 순간** `XamlParseException` 으로 죽는다.
> 빌드 출력(`bin/`)에는 그 파일이 있기 때문에 `dotnet run` 으로는 절대 재현되지 않는다.
> 앱은 여전히 unpackaged(`WindowsPackageType=None`)이며, 이 속성은 리소스 인덱스 생성만 켠다.

## 성능 측정

```bash
dotnet run --project tests/DeskNote.Benchmarks -c Release
```

10,000개 노트를 시드하고 보고서 p13-14의 SLO를 재며, 목표를 놓치면 종료 코드 1을 돌려준다.
측정값은 [architecture.md](architecture.md#성능-측정)에 있다.

강제 종료 내성은 같은 프로젝트의 `--crash-writer <db 경로>` 모드로 잰다.

## 앱 아이콘

아이콘은 그림 파일이 아니라 스크립트다. 크기마다 새로 그리기 때문에 — 16px 트레이 렌더링은 256px
셸 렌더링보다 글줄이 적고 획이 두껍다 — 한 장을 축소해 만드는 방식으로는 나오지 않는다.

```bash
python assets/icon/build_icon.py
```

`src/DeskNote.App/Assets/DeskNote.ico` (9개 크기)와 `assets/icon/preview.png` (밝은/어두운 배경
대조표)를 다시 만든다. Pillow만 있으면 된다.

아이콘이 앱에 붙는 경로는 세 갈래다.

| 어디 | 방법 |
|---|---|
| exe · 작업표시줄 · Alt-Tab | csproj의 `ApplicationIcon` (아이콘이 exe 리소스로 들어간다) |
| 트레이 | `TrayIconService` 가 `AppIcon.LoadSmall()` 로 실행 모듈에서 읽는다 |
| 각 창 | 창 생성자의 `AppIcon.Apply(this)` |

`AppIcon` 은 디스크 경로가 아니라 **실행 중인 모듈의 아이콘 리소스**(ordinal 32512)에서 읽는다.
빌드 산출물과 사용자 PC 사이에서 파일이 사라질 여지를 없애기 위해서다.

## 문서 이미지

`docs/images/` 의 화면 사진은 데모용 메모를 넣은 별도 데이터베이스로 앱을 띄우고, 창 단위로
`PrintWindow` 해서 만들었다. 바탕화면 전체를 찍지 않는 이유는 사진에 남의 화면이 들어가지 않게
하기 위해서이고, 창 단위로 찍으면 다른 창에 가려 있어도 그대로 나온다.

다시 만들 일이 생기면 주의할 점은 둘이다. 메모의 조작 요소는 포인터가 **실제로 창 경계를 넘어야**
나타나므로 커서를 창 밖으로 뺐다가 넣어야 하고, 캡처는 `GetWindowRect` 크기로 찍은 뒤 DWM 프레임
경계만큼 잘라내야 창의 오른쪽·아래 테두리가 살아난다.

데모용 데이터베이스는 사진에 실제 사용자의 메모가 들어가지 않게 하려는 것이다. 앱이 `--data <경로>`
(또는 `DESKNOTE_DATA`)를 받으므로 실제 설치본 옆에서 별도의 데이터로 인스턴스를 하나 더 띄울 수 있고,
단일 인스턴스 뮤텍스도 데이터 폴더별로 잡히므로 평소 쓰던 앱을 끄지 않아도 된다.

`LOCALAPPDATA` 환경 변수를 바꾸는 방법은 동작하지 않는다.
`Environment.GetFolderPath(LocalApplicationData)` 는 그 변수가 아니라 셸의 known folder를 읽는다.

```powershell
$demo = "$env:TEMP\desknote-docs"
dotnet run --project tools/DemoSeeder -- $demo
Start-Process .\dist\DeskNote-win-x64\DeskNote.exe -ArgumentList "--data", $demo
```

`tools/DemoSeeder` 는 지어낸 메모 일곱 개와 노트북 둘을 넣고 펫을 켜 둔다. 펫은 `움직이기` 를 끈
상태로 시작하는데, **걷는 펫은 창 위치를 읽고 포인터가 도착하는 사이에 자리를 옮겨** 클릭이 빗나가기
때문이다.

창은 `tools/capture-window.ps1` 로 찍고(창 단위 `PrintWindow` + DWM 프레임 기준 자르기),
`tools/point-window.ps1` 로 포인터를 넣거나 누른다. 포인터는 대상 창의 rect 안으로만 이동하며 끝나면
원래 자리로 돌아간다. 메모의 조작 요소는 `SetCursorPos` 로 커서를 순간이동시키면 나타나지 않는다 —
WinUI는 커서의 위치가 아니라 **입력 큐**를 보므로 실제 입력 이벤트로 움직여야 한다.

**UI를 바꾸면 그 화면의 이미지도 함께 다시 찍는다.** `docs/images/` 는 추적되는 파일이고, 낡은 사진은
없는 사진보다 나쁘다 — README를 읽는 사람은 사진 쪽을 믿는다.

아직 없는 것은 **메모 위에 그려진 이미지 첨부** 한 장이다. 시더가 첨부 레코드까지 넣도록 하면
클릭 없이 찍을 수 있다.
