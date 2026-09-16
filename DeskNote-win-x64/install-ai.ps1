<#
.SYNOPSIS
    DeskNote의 로컬 AI(Ollama + 모델 두 개)를 설치하고 연결 상태를 확인한다.

.DESCRIPTION
    보통은 이 파일을 직접 실행하지 않고 옆의 install-ai.bat 을 더블클릭한다.
    하는 일은 다섯 가지뿐이고, 이미 되어 있는 단계는 건너뛴다.

      1. Ollama 가 설치돼 있는지 확인하고, 없으면 설치한다 (winget → 공식 설치 파일 순).
      2. 로컬 추론 서버(기본 http://localhost:11434)가 떠 있는지 확인하고, 없으면 띄운다.
      3. 대화용 모델을 받는다 (기본 gemma4:e2b — 요약 · 재작성 · 질문).
      4. 임베딩 모델을 받는다 (기본 bge-m3 — 의미 검색 · 관련 메모).
      5. 서버에 실제로 올라와 있는지 다시 확인하고 결과를 요약한다.

    네트워크로 나가는 것은 위 두 모델을 내려받을 때뿐이다. 이 스크립트는 메모를 읽지도
    보내지도 않는다.

.PARAMETER Model
    대화용 모델 태그. DeskNote 설정의 ai.model 과 같아야 한다.

.PARAMETER EmbeddingModel
    임베딩 모델 태그. DeskNote 설정의 ai.embeddingModel 과 같아야 한다.

.PARAMETER Endpoint
    로컬 추론 서버 주소. DeskNote 설정의 ai.endpoint 와 같아야 한다.

.PARAMETER CheckOnly
    아무것도 설치하지 않고 지금 상태만 진단한다.

.PARAMETER Yes
    확인 질문 없이 진행한다 (무인 설치용).

.EXAMPLE
    .\install-ai.ps1
    기본 모델 두 개를 설치한다.

.EXAMPLE
    .\install-ai.ps1 -CheckOnly
    지금 AI가 왜 안 되는지만 진단한다.

.EXAMPLE
    .\install-ai.ps1 -Model llama3.2:3b -Yes
    다른 대화 모델로, 질문 없이 설치한다. DeskNote 의 ai.model 도 같이 바꿔야 한다.
#>
[CmdletBinding()]
param(
    [string] $Model = 'gemma4:e2b',
    [string] $EmbeddingModel = 'bge-m3',
    [string] $Endpoint = 'http://localhost:11434',
    [switch] $CheckOnly,
    [switch] $Yes
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$global:LASTEXITCODE = 0

# 콘솔이 한글을 그대로 그리도록. 실패해도 진행에는 지장이 없다.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }

# Windows PowerShell 5.1 은 기본 보안 프로토콜이 낡아 다운로드가 조용히 실패할 수 있다.
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$OllamaDownloadPage = 'https://ollama.com/download/windows'
$OllamaInstallerUri = 'https://ollama.com/download/OllamaSetup.exe'

function Write-Title { param([string] $Text) Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Step { param([string] $Text) Write-Host "   $Text" }
function Write-Ok { param([string] $Text) Write-Host "   [완료] $Text" -ForegroundColor Green }
function Write-Note { param([string] $Text) Write-Host "   [주의] $Text" -ForegroundColor Yellow }
function Write-Bad { param([string] $Text) Write-Host "   [실패] $Text" -ForegroundColor Red }

function Get-PropertyValue {
    param($InputObject, [string] $Name)

    if ($null -eq $InputObject) { return $null }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Confirm-Step {
    param([string] $Question)

    if ($Yes) { return $true }

    while ($true) {
        $answer = Read-Host "   $Question (Y/N)"
        if ($answer -match '^\s*(y|yes|예)\s*$') { return $true }
        if ($answer -match '^\s*(n|no|아니오)\s*$') { return $false }
    }
}

function Get-OllamaPath {
    $command = Get-Command 'ollama.exe' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $roots = @()
    if ($env:LOCALAPPDATA) { $roots += (Join-Path $env:LOCALAPPDATA 'Programs\Ollama') }
    if ($env:ProgramFiles) { $roots += (Join-Path $env:ProgramFiles 'Ollama') }
    if (${env:ProgramFiles(x86)}) { $roots += (Join-Path ${env:ProgramFiles(x86)} 'Ollama') }

    foreach ($root in $roots) {
        $candidate = Join-Path $root 'ollama.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    return $null
}

function Update-PathFromRegistry {
    # 설치 직후에는 이 창의 PATH 가 아직 옛것이다. 레지스트리에서 다시 읽어 온다.
    $parts = @(
        [Environment]::GetEnvironmentVariable('Path', 'Machine')
        [Environment]::GetEnvironmentVariable('Path', 'User')
    ) | Where-Object { $_ }

    if ($parts.Count -gt 0) { $env:Path = ($parts -join ';') }
}

function Test-OllamaServer {
    param([int] $TimeoutSeconds = 3)

    try {
        $null = Invoke-RestMethod -Uri "$Endpoint/api/tags" -TimeoutSec $TimeoutSeconds
        return $true
    }
    catch {
        return $false
    }
}

function Get-InstalledModel {
    try {
        $response = Invoke-RestMethod -Uri "$Endpoint/api/tags" -TimeoutSec 10
    }
    catch {
        return @()
    }

    $models = Get-PropertyValue $response 'models'
    if ($null -eq $models) { return @() }
    return @($models)
}

function Get-NormalizedTag {
    param([string] $Tag)

    # DeskNote 와 같은 규칙: 태그가 없으면 :latest 로 본다 (OllamaAiService.Normalize).
    $trimmed = $Tag.Trim()
    if (-not $trimmed.Contains(':')) { $trimmed = $trimmed + ':latest' }
    return $trimmed.ToLowerInvariant()
}

function Test-ModelInstalled {
    param([string] $Tag, $Models)

    $wanted = Get-NormalizedTag $Tag
    foreach ($installedModel in $Models) {
        $name = Get-PropertyValue $installedModel 'name'
        if ($name -and (Get-NormalizedTag $name) -eq $wanted) { return $true }
    }

    return $false
}

function Format-Size {
    param([double] $Bytes)

    if ($Bytes -ge 1GB) { return ('{0:N1} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N0} MB' -f ($Bytes / 1MB)) }
    return ('{0:N0} bytes' -f $Bytes)
}

function Get-ModelStoreFreeBytes {
    $root = $env:OLLAMA_MODELS
    if (-not $root) { $root = Join-Path $env:USERPROFILE '.ollama' }

    try {
        $qualifier = [System.IO.Path]::GetPathRoot($root)
        if (-not $qualifier) { return $null }
        $drive = Get-PSDrive -Name $qualifier.Substring(0, 1) -ErrorAction SilentlyContinue
        return (Get-PropertyValue $drive 'Free')
    }
    catch {
        return $null
    }
}

function Install-Ollama {
    $winget = Get-Command 'winget.exe' -ErrorAction SilentlyContinue
    if ($winget) {
        Write-Step 'winget 으로 설치합니다. 설치 관리자 창이 뜨면 허용해 주세요.'
        & $winget.Source install --id Ollama.Ollama --exact --source winget --accept-package-agreements --accept-source-agreements
        Update-PathFromRegistry

        # winget 은 "이미 설치됨" 에도 0 이 아닌 코드를 준다. 판단은 실제 파일로 한다.
        if (Get-OllamaPath) { return $true }
        Write-Note 'winget 으로는 설치되지 않았습니다. 공식 설치 파일로 다시 시도합니다.'
    }
    else {
        Write-Step 'winget 이 없어서 공식 설치 파일을 내려받습니다.'
    }

    Write-Step "받을 파일: $OllamaInstallerUri"
    if (-not (Confirm-Step '내려받아 설치 프로그램을 실행할까요?')) {
        Write-Note "직접 받으시려면: $OllamaDownloadPage"
        try { Start-Process $OllamaDownloadPage } catch { }
        return $false
    }

    $installerPath = Join-Path $env:TEMP 'OllamaSetup.exe'
    $previousProgress = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'   # 진행 막대를 그리면 다운로드가 몇 배 느려진다.
        Invoke-WebRequest -Uri $OllamaInstallerUri -OutFile $installerPath
    }
    catch {
        Write-Bad "설치 파일을 내려받지 못했습니다: $($_.Exception.Message)"
        Write-Step "브라우저에서 직접 받으세요: $OllamaDownloadPage"
        return $false
    }
    finally {
        $ProgressPreference = $previousProgress
    }

    Write-Step '설치 프로그램을 실행합니다. 창이 닫힐 때까지 기다립니다.'
    Start-Process -FilePath $installerPath -Wait
    Update-PathFromRegistry

    return [bool](Get-OllamaPath)
}

function Start-OllamaServer {
    param([string] $OllamaPath)

    $directory = Split-Path -Parent $OllamaPath
    $trayApp = Join-Path $directory 'ollama app.exe'

    if (Test-Path -LiteralPath $trayApp -PathType Leaf) {
        Start-Process -FilePath $trayApp | Out-Null
    }
    else {
        Start-Process -FilePath $OllamaPath -ArgumentList 'serve' -WindowStyle Hidden | Out-Null
    }

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if (Test-OllamaServer) { return $true }
        Start-Sleep -Seconds 2
    }

    return $false
}

function Invoke-ModelPull {
    param([string] $OllamaPath, [string] $Tag, [string] $Purpose)

    Write-Step "받는 중: $Tag  ($Purpose)"
    Write-Step '진행률은 Ollama 가 아래에 직접 표시합니다. 처음 받을 때는 수 GB 입니다.'
    & $OllamaPath pull $Tag
    if ($LASTEXITCODE -eq 0) {
        Write-Ok "$Tag 준비 완료"
        return $true
    }

    Write-Bad "$Tag 를 받지 못했습니다. 종료 코드 $LASTEXITCODE"
    Write-Step '모델 이름이 맞는지, 인터넷이나 회사 프록시가 막고 있지 않은지 확인하세요.'
    Write-Step '쓸 수 있는 이름은 https://ollama.com/library 에서 확인합니다.'
    return $false
}

# ---------------------------------------------------------------------------

Write-Host ''
Write-Host '  DeskNote 로컬 AI 설치' -ForegroundColor White
Write-Host '  ----------------------------------------------------------------'
Write-Host '  이 PC 안에서만 도는 AI를 준비합니다. 메모는 밖으로 나가지 않습니다.'
Write-Host "  대화 모델   : $Model"
Write-Host "  임베딩 모델 : $EmbeddingModel"
Write-Host "  서버 주소   : $Endpoint"
if ($CheckOnly) { Write-Host '  모드        : 진단만 (아무것도 설치하지 않습니다)' -ForegroundColor Yellow }

# 기본이 아닌 주소를 골랐다면 ollama 명령도 같은 곳을 보게 한다.
if ($Endpoint -ne 'http://localhost:11434') { $env:OLLAMA_HOST = $Endpoint }

$failures = @()

Write-Title '1/5 · Ollama 설치 확인'
$ollamaPath = Get-OllamaPath
if ($ollamaPath) {
    Write-Ok "이미 설치돼 있습니다: $ollamaPath"
}
elseif ($CheckOnly) {
    Write-Bad 'Ollama 가 설치돼 있지 않습니다.'
    $failures += 'Ollama 미설치'
}
else {
    Write-Step 'Ollama 가 없습니다. 지금 설치합니다.'
    if (Install-Ollama) {
        $ollamaPath = Get-OllamaPath
        Write-Ok "설치했습니다: $ollamaPath"
    }
    else {
        Write-Bad 'Ollama 설치를 마치지 못했습니다.'
        Write-Step "설치한 뒤 이 파일을 다시 실행하세요: $OllamaDownloadPage"
        $failures += 'Ollama 설치 실패'
    }
}

if (-not $ollamaPath) {
    Write-Host ''
    Write-Bad 'Ollama 없이는 다음 단계를 진행할 수 없습니다.'
    Write-Host '   DeskNote 의 메모 · 검색 · 알림은 AI 없이도 그대로 동작합니다.'
    exit 1
}

Write-Title '2/5 · 로컬 추론 서버 확인'
if (Test-OllamaServer) {
    Write-Ok "서버가 응답합니다: $Endpoint"
}
elseif ($CheckOnly) {
    Write-Bad "서버가 응답하지 않습니다: $Endpoint"
    $failures += '서버 미실행'
}
else {
    Write-Step '서버가 응답하지 않아 지금 띄웁니다. 최대 1분 기다립니다.'
    if (Start-OllamaServer -OllamaPath $ollamaPath) {
        Write-Ok "서버가 올라왔습니다: $Endpoint"
    }
    else {
        Write-Bad "서버가 응답하지 않습니다: $Endpoint"
        Write-Step '다른 창에서 ollama serve 를 직접 실행한 뒤 이 파일을 다시 실행하세요.'
        Write-Host ''
        exit 1
    }
}

$installedModels = Get-InstalledModel
$chatInstalled = Test-ModelInstalled -Tag $Model -Models $installedModels
$embeddingInstalled = Test-ModelInstalled -Tag $EmbeddingModel -Models $installedModels

if (-not $CheckOnly -and -not ($chatInstalled -and $embeddingInstalled)) {
    $free = Get-ModelStoreFreeBytes
    if ($null -ne $free) {
        Write-Step "모델을 저장할 드라이브의 여유 공간: $(Format-Size $free)"
        if ($free -lt 12GB) {
            Write-Note '모델 두 개에 10GB 안팎이 필요합니다. 공간이 부족할 수 있습니다.'
            if (-not (Confirm-Step '그래도 계속할까요?')) { exit 1 }
        }
    }
}

Write-Title "3/5 · 대화 모델 ($Model)"
if ($chatInstalled) {
    Write-Ok '이미 받아져 있습니다.'
}
elseif ($CheckOnly) {
    Write-Bad '아직 받지 않았습니다. AI 타일이 비활성으로 보이는 이유입니다.'
    $failures += "대화 모델 없음 ($Model)"
}
elseif (-not (Invoke-ModelPull -OllamaPath $ollamaPath -Tag $Model -Purpose '요약 · 재작성 · 질문')) {
    $failures += "대화 모델 받기 실패 ($Model)"
}

Write-Title "4/5 · 임베딩 모델 ($EmbeddingModel)"
if ($embeddingInstalled) {
    Write-Ok '이미 받아져 있습니다.'
}
elseif ($CheckOnly) {
    Write-Bad '아직 받지 않았습니다. 의미 검색과 관련 메모만 빠집니다.'
    $failures += "임베딩 모델 없음 ($EmbeddingModel)"
}
elseif (-not (Invoke-ModelPull -OllamaPath $ollamaPath -Tag $EmbeddingModel -Purpose '의미 검색 · 관련 메모')) {
    $failures += "임베딩 모델 받기 실패 ($EmbeddingModel)"
}

Write-Title '5/5 · 서버에 올라온 모델 확인'
$installedModels = Get-InstalledModel
if ($installedModels.Count -eq 0) {
    Write-Bad '서버가 가진 모델 목록을 읽지 못했습니다.'
    $failures += '모델 목록 확인 실패'
}
else {
    # 반복 변수 이름은 $Model 을 피한다. 그 이름은 [string] 매개변수라 객체를 담으면 문자열로 굳는다.
    foreach ($installedModel in $installedModels) {
        $name = Get-PropertyValue $installedModel 'name'
        $size = Get-PropertyValue $installedModel 'size'
        if ($null -eq $size) { $size = 0 }
        Write-Step ('{0,-28} {1}' -f $name, (Format-Size $size))
    }

    Write-Host ''
    if (Test-ModelInstalled -Tag $Model -Models $installedModels) {
        Write-Ok "DeskNote 가 찾는 대화 모델이 있습니다: $Model"
    }
    else {
        Write-Bad "DeskNote 가 찾는 대화 모델이 없습니다: $Model"
        Write-Step 'DeskNote 의 ai.model 값과 이 이름이 정확히 같아야 AI 타일이 켜집니다.'
        if (-not $CheckOnly) { $failures += "대화 모델 없음 ($Model)" }
    }

    if (Test-ModelInstalled -Tag $EmbeddingModel -Models $installedModels) {
        Write-Ok "임베딩 모델이 있습니다: $EmbeddingModel"
    }
    else {
        Write-Note "임베딩 모델이 없습니다: $EmbeddingModel — 의미 검색과 관련 메모만 빠집니다."
    }
}

Write-Host ''
Write-Host '  ----------------------------------------------------------------'
if ($failures.Count -eq 0) {
    Write-Host '  준비가 끝났습니다.' -ForegroundColor Green
    Write-Host ''
    Write-Host '  DeskNote 에서 확인하는 법'
    Write-Host '    1. 메모 위에 포인터를 올리고 [AI] 단추를 누릅니다.'
    Write-Host '    2. 메뉴 맨 아래에 모델 이름과 실행 위치(CPU 또는 GPU)가 보이면 연결된 것입니다.'
    Write-Host ''
    Write-Host '  앱을 다시 시작하지 않아도 됩니다. DeskNote 는 1분마다 다시 확인합니다.'
    Write-Host '  첫 호출은 모델을 메모리에 올리느라 1분 가까이 걸릴 수 있습니다. 그 다음부터는 빠릅니다.'
    exit 0
}

Write-Host '  끝나지 않은 것이 있습니다:' -ForegroundColor Yellow
foreach ($failure in $failures) { Write-Host "    - $failure" -ForegroundColor Yellow }
Write-Host ''
Write-Host '  메모 · 검색 · 알림 · 기록은 AI 없이도 그대로 동작합니다.'
Write-Host '  다시 시도하려면 이 파일을 한 번 더 실행하세요. 이미 끝난 단계는 건너뜁니다.'
exit 1
