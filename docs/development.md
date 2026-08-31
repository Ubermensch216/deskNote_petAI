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

`main` 과 pull request는 Windows에서 복원·서식 검증·Release 빌드·테스트·win-x64 단일 파일 publish를 순서대로
실행한다. 로컬과 CI가 같은 진입점을 쓰도록 별도 테스트 스크립트를 두지 않는다.

CI가 실패한 변경은 병합하지 않는다. 특히 테스트 성공 뒤 publish까지 실행하는 이유는 WinUI 앱이
`dotnet run`에서는 정상이어도 publish 출력에서 리소스 인덱스를 잃을 수 있기 때문이다.
테스트 뒤에는 10,000개 메모 성능 게이트도 실행해 저장·검색·복원 경로의 회귀를 막는다.

## 실행 파일 만들기

배포용 실행 파일은 하나짜리 self-contained exe다. .NET 런타임도 Windows App SDK도 그 안에 들어가므로
받는 쪽은 아무것도 설치하지 않는다.

```bash
dotnet publish src/DeskNote.App/DeskNote.App.csproj -c Release -r win-x64 -o dist/single -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
```

`dist/single/DeskNote.exe` 하나가 나오고, 크기는 약 220MB다. 첫 실행에서 네이티브 파일을 임시 폴더에
풀기 때문에 그때만 시작이 몇 초 느리다.

받는 쪽에 필요한 것은 Windows 10 1809 이상(x64)과 Visual C++ 재배포 가능 패키지뿐이다. 후자는
self-contained Windows App SDK가 요구하는 유일한 시스템 구성 요소이고 publish 출력에 들어가지 않는다.

폴더 형태(파일 440개, 약 226MB)로 내보내려면 단일 파일 옵션만 빼면 된다. 첫 실행이 빠르고,
디버깅이나 파일 단위 배포에 쓴다.

```bash
dotnet publish src/DeskNote.App/DeskNote.App.csproj -c Release -r win-x64 -o dist/DeskNote-win-x64
```

ARM64 기기용은 `-r win-arm64` 로 바꿔서 같은 명령을 쓴다.

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
