[CmdletBinding()]
param(
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distributionRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'dist'))
$outputDirectory = [System.IO.Path]::GetFullPath((Join-Path $distributionRoot 'DeskNote-win-x64'))
$archivePath = [System.IO.Path]::GetFullPath((Join-Path $distributionRoot 'DeskNote-win-x64.zip'))
$expectedPrefix = $distributionRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $outputDirectory.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean an output directory outside dist: $outputDirectory"
}

New-Item -ItemType Directory -Path $distributionRoot -Force | Out-Null
if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

$publishArguments = @(
    'publish'
    (Join-Path $repositoryRoot 'src\DeskNote.App\DeskNote.App.csproj')
    '--configuration', 'Release'
    '--runtime', 'win-x64'
    '--output', $outputDirectory
    '-p:PublishSingleFile=false'
    '-p:DebugType=none'
)

if ($NoRestore) {
    $publishArguments += '--no-restore'
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$requiredFiles = @(
    'DeskNote.exe'
    'DeskNote.dll'
    'DeskNote.pri'
    'Microsoft.ui.xaml.dll'
    'Microsoft.WindowsAppRuntime.dll'
)

foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $outputDirectory $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Published package is missing required file: $requiredFile"
    }
}

$appPath = Join-Path $outputDirectory 'DeskNote.exe'
$process = Start-Process -FilePath $appPath -WorkingDirectory $outputDirectory -PassThru

try {
    Start-Sleep -Seconds 8
    $process.Refresh()
    if ($process.HasExited) {
        throw "Packaged DeskNote exited during startup with code $($process.ExitCode)"
    }
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }

    $process.Dispose()
}

$compressionAttempts = 5
for ($attempt = 1; $attempt -le $compressionAttempts; $attempt++) {
    try {
        if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }

        Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
        break
    }
    catch {
        if ($attempt -eq $compressionAttempts) {
            throw
        }

        Write-Warning "Package files are still being released; retrying archive ($attempt/$compressionAttempts)."
        Start-Sleep -Seconds 2
    }
}

$archive = Get-Item -LiteralPath $archivePath
Write-Host "Created verified package: $($archive.FullName) ($($archive.Length) bytes)"
