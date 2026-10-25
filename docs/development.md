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

```powershell
./scripts/publish-win-x64.ps1
```

만들어지는 것은 둘이고, 받는 사람에게 건네는 것은 **`dist/DeskNote.exe` 하나**다.

| 파일 | 크기 | 무엇인가 |
|---|---:|---|
| `dist/DeskNote.exe` | 약 80MB | 앱을 품은 실행 파일 하나. 처음 실행할 때 `%LOCALAPPDATA%\Programs\DeskNote` 에 풀고 시작한다 |
| `dist/DeskNote-win-x64.zip` | 약 80MB | 같은 앱을 폴더로 담은 포터블판. 푼 폴더에서 바로 실행한다 |

두 파일의 내용은 같다. 앞의 것은 뒤의 것을 안에 품고 있다.

스크립트는 `dist/DeskNote-win-x64/` 폴더를 만들고, 필수 파일을 확인하고, **그 폴더의 앱을 8초간
실행**해 시작 중 죽지 않는지 본다. 그 뒤 zip으로 압축하고, 그 zip을 품은 `DeskNote.exe` 를 만든 다음,
**그 exe도 처음 받는 사람이 하는 대로** 검사한다 — 빈 폴더로 향하게 해 실행하고(`DESKNOTE_INSTALL_DIR`),
앱이 풀리고, 풀린 앱이 8초를 버티는지. 두 검사 모두 자기 데이터 폴더(`--data`)를 쓰므로 개발 중인
앱이나 실제 메모를 건드리지 않는다. CI도 같은 스크립트를 사용한다.

### 어떻게 작아지고, 어떻게 하나가 되는가

**하나가 되는 방법.** 앱 자체를 단일 파일로 publish할 수는 없다(아래 절). 그래서 `tools/Launcher` 가
작은 .NET Framework 4.7.2 프로그램을 만들고, 그 안에 publish된 앱의 zip을 리소스로 넣는다. 4.7.2는
Windows 10 1803부터 들어 있으므로 이 프로그램은 아무것도 설치되지 않은 PC에서, 그리고 앱이 지원하는
모든 Windows(1809 이상)에서 뜬다. .NET 10으로 만들면 그 시작 단계에서 .NET 10을 먼저 요구하게 된다.

런처가 하는 일은 셋이다. (1) 앱이 이미 풀려 있고 같은 빌드면 곧바로 시작한다 — **첫 실행 뒤로는 파일
하나를 확인하고 프로세스 하나를 띄우는 것이 전부**라서 폼 라이브러리조차 올리지 않는다. (2) 아니면
진행 창을 띄우고 풀되, 옆 폴더에 **완전히 쓴 다음에 폴더째 바꾼다**. 도중에 전원이 나가거나 디스크가
차도 이전 버전이 그대로 남는다. (3) 실행 중인 이전 버전은 닫는다 — 열린 파일이 있는 폴더는 Windows가
옮겨 주지 않는다. 풀리는 곳이 **고정된 폴더**인 데에는 이유가 있다. Windows 시작 시 실행은 실행 중인
exe의 경로를 레지스트리에 적는데, 버전마다 달라지는 경로면 업데이트 한 번에 그 항목이 끊어진다.
`--install-ai` 는 풀린 폴더의 `install-ai.bat` 을 열어 준다.

**작아지는 방법.** 처음에는 245MB(압축 후 104MB)였다. 줄인 것은 셋이다.

- `Microsoft.WindowsAppSDK` 메타패키지 대신 **필요한 구성요소만** 참조한다(`Base`, `Foundation`,
  `InteractiveExperiences`, `WinUI`, `DWrite`, `Runtime`). 메타패키지는 AI·ML·Search·Widgets까지
  끌어오는데, 이 앱은 그 어느 것도 쓰지 않는다. ONNX Runtime과 DirectML만 38MB였다.
  **Windows App SDK 버전을 올릴 때는 메타패키지의 의존 목록을 보고 여섯 줄의 버전을 함께 올린다.**
- 디버거용 런타임 파일(`mscordaccore*`, `mscordbi`)과 PDB 판독기(`Microsoft.DiaSymReader.Native`)를
  뺀다. 실행 중에 아무도 읽지 않는다.
- zip은 가능하면 `SmallestSize` 로 압축한다(PowerShell 7). Windows PowerShell 5.1에서는 `Optimal`.

남은 80MB의 대부분은 .NET 런타임(CoreLib·coreclr·System.*, 약 17MB), Windows SDK 투영
(`Microsoft.Windows.SDK.NET.dll`, 6MB), WinUI(약 14MB), 캐릭터 PNG(15MB)다. 더 줄일 수 있는 길은
있지만 위험이 붙는다.

- **트리밍(`PublishTrimmed`)**: 런타임과 Windows SDK 투영에서 20~30MB 더 줄겠지만, WinUI와 CsWinRT는
  리플렉션과 이름으로 찾는 활성화를 쓰고 Dapper도 그렇다. 시작은 되는데 특정 화면에서만 죽는 형태로
  깨지기 쉬워서, 모든 화면을 돌려 보는 검사가 생기기 전에는 켜지 않는다.
- **PNG 팔레트화**: 스프라이트 시트를 256색으로 줄이면 15MB가 5MB가 되지만 그림이 바뀐다.
  원본은 그대로 두고 publish 단계에서만 바꾸는 방식이어야 한다.

받는 쪽에 필요한 것은 Windows 10 1809 이상(x64)과 Visual C++ 재배포 가능 패키지뿐이다. 후자는
self-contained Windows App SDK가 요구하는 유일한 시스템 구성 요소이고 publish 출력에 들어가지 않는다.
`scripts/install-ai.bat` 과 `scripts/install-ai.ps1` 도 앱과 함께 풀린다. 받는 사람은 저장소를 볼 일이
없고, 그에게 로컬 AI 준비는 "`install-ai.bat` 을 누른다" 한 줄이어야 하기 때문이다. 스크립트가 하는
일과 옵션은 [README의 AI 설치 장](../README.md#ai-설치하고-연결하기)에 있다. 배치 파일은 실행 로직을
갖지 않는 실행기일 뿐이다 — 한글 메시지는 BOM이 붙은 `.ps1` 안에 두고, `.bat` 은 ASCII만 담는다.
콘솔 코드 페이지가 무엇이든 안내가 깨지지 않게 하려는 것이다.

### 앱 자체를 단일 exe로 만들지 않는 이유

.NET 10에서 Windows App SDK 네이티브 파일을 하나의 exe에 묶으면, 일부 환경에서 WinUI 활성화가
시작되기 전에 `0x80040111 (ClassFactory cannot supply requested class)`로 종료된다. Microsoft가 추적
중인 [WindowsAppSDK #6058](https://github.com/microsoft/WindowsAppSDK/issues/6058)과 같은 증상이다.
거기에 더해 WinUI는 리소스 인덱스(`DeskNote.pri`)와 이미지를 **exe 옆의 파일로** 찾으므로, 묶은 파일
안에서는 그것들도 풀어 줘야 한다. 그래서 앱은 폴더형 self-contained 로 두고, 하나로 만드는 일은
그 바깥의 런처가 맡는다. 앱이 단일 파일 publish를 지원하게 되면 이 런처는 필요 없어진다.

ARM64 프로젝트 빌드는 지원하지만 배포 패키지와 시작 검사는 아직 win-x64만 자동화한다. ARM64 배포를
추가할 때는 별도 하드웨어에서 같은 시작 검사를 통과시킨 뒤 전용 파일로 제공한다.

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

## 펫 수면 스프라이트

자는 자세도 그림 파일이 아니라 스크립트다.

```bash
python assets/companion/build_sleep_sprites.py
```

`src/DeskNote.App/Assets/Companion/{종}-sleep.png` 여섯 장(오리 제외)을 각 종의 `-rest.png` 에서 다시
만든다. 눈을 감기고 몸을 눕히는 두 가지 연산이며, 근거는
[docs/companion.md](companion.md#바탕화면-행동)에 있다. Pillow와 NumPy가 필요하다. 결과물은
커밋되므로 앱을 빌드하는 사람이 이것을 돌릴 일은 없고, `-rest.png` 를 바꿨을 때만 다시 돌린다.

스크립트는 만들어 낸 그림의 위·아래 경계를 출력한다. 그 값이 `DesktopPetWindow` 의
`SleepHeadTopFraction` 계열 상수의 출처이므로, 자세를 조정했으면 출력된 값으로 상수도 맞춰야
성장 표식과 말풍선이 누운 펫의 머리를 따라간다.

오리는 이 스크립트에 들어 있지 않다. 눈이 하이라이트 없는 점이라 눈 찾기가 통하지 않고, 처음부터
선 그림과 누운 그림 두 장으로 들어왔기 때문이다. 오리의 세 시트는 따로 만든다.

```bash
python assets/companion/build_duck_sprites.py
```

원본 두 장은 `assets/companion/duck/` 에 있다. `duck-walk.png` 는 선 그림을 발을 축으로 좌우로 흔든
뒤뚱걸음, `duck-rest.png` 는 같은 그림의 호흡과 눈 깜빡임, `duck-sleep.png` 는 누운 그림에 감은 눈을
그린 것이다. 모두 회전과 균일 확대만 쓰므로 비율 규칙을 지킨다. 출력하는 위·아래 경계는 다른 종과
같은 값(발 0.78, 선 머리 0.21, 누운 머리 0.36)에 맞춰져 있어 `DesktopPetWindow` 의 상수를 바꿀 필요가
없다.

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

`tools/DemoSeeder` 는 지어낸 메모 일곱 개와 노트북 둘을 넣고 펫을 켜 둔다. `--motion` 을 주면 펫을 걷는
채로 두고(움직임을 확인할 때), `--pet <키>` 로 종을 고른다. 펫은 `움직이기` 를 끈
상태로 시작하는데, **걷는 펫은 창 위치를 읽고 포인터가 도착하는 사이에 자리를 옮겨** 클릭이 빗나가기
때문이다.

### 그려서 움직이는 캐릭터의 시트

손오공·어린 오공·무천도사·베지터·피콜로는 바탕화면에서 매 프레임 그려지지만, 설정·대시보드·성장 안내는
다른 펫처럼 `Assets/Companion/{키}-walk|rest|sleep.png` 를 한 칸 잘라 쓴다. 캐릭터를 고치면 시트를 다시
만든다.

```powershell
dotnet run --project tools/PetSheets -- sheets src/DeskNote.App/Assets/Companion
dotnet run --project tools/PetSheets -- contact $env:TEMP\pet-contact
```

`sheets` 는 세 시트를 쓰고 `PetArtBounds` 에 붙일 다섯 줄을 출력한다. `contact` 는 모든 상태와 15가지
시그니처를 시간 순으로 늘어놓은 점검용 그림을 만든다 — 자세를 고친 뒤에는 이 그림을 눈으로 본다.

창은 `tools/capture-window.ps1` 로 찍고(창 단위 `PrintWindow` + DWM 프레임 기준 자르기),
`tools/point-window.ps1` 로 포인터를 넣거나 누른다. 포인터는 대상 창의 rect 안으로만 이동하며 끝나면
원래 자리로 돌아간다. 메모의 조작 요소는 `SetCursorPos` 로 커서를 순간이동시키면 나타나지 않는다 —
WinUI는 커서의 위치가 아니라 **입력 큐**를 보므로 실제 입력 이벤트로 움직여야 한다.

한 화면보다 긴 창은 두 장으로 나눠 찍는다. 설정 창(`settings-character.png`,
`settings-behavior.png`)이 그런 경우로, 기본 크기 그대로 두고 휠로 위·아래 끝까지 굴린 뒤 각각
찍었다. 창을 `MoveWindow` 로 늘려 한 장에 담으려고 하면 안 된다 — `AppWindow` 가 모르는 사이에
크기가 바뀌어 그 뒤의 활성화가 이상하게 동작한다.

**UI를 바꾸면 그 화면의 이미지도 함께 다시 찍는다.** `docs/images/` 는 추적되는 파일이고, 낡은 사진은
없는 사진보다 나쁘다 — README를 읽는 사람은 사진 쪽을 믿는다.

아직 없는 것은 **메모 위에 그려진 이미지 첨부** 한 장이다. 시더가 첨부 레코드까지 넣도록 하면
클릭 없이 찍을 수 있다.
