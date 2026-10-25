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
$singleFilePath = [System.IO.Path]::GetFullPath((Join-Path $distributionRoot 'DeskNote.exe'))
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

# Files the self-contained publish copies that nothing at run time reads: the debugger's view into
# the runtime, and the reader for Windows symbol files (the build above has none). Together they
# are a few megabytes in every download.
$unneeded = @(
    'mscordaccore.dll'
    'mscordaccore_*.dll'
    'mscordbi.dll'
    'Microsoft.DiaSymReader.Native.*.dll'
)
foreach ($pattern in $unneeded) {
    Get-ChildItem -LiteralPath $outputDirectory -Filter $pattern -File | Remove-Item -Force
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

# The AI installer ships beside the app: the person who unzips this never sees the repository,
# and "run install-ai.bat" is the whole local-AI setup for them.
foreach ($installerFile in @('install-ai.bat', 'install-ai.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $installerFile) -Destination $outputDirectory -Force
}

# Starts a copy of the app, waits for it to survive its own start-up, and stops it. It is run
# against a data folder of its own: a packaging check has no business touching anybody's notes,
# or finding the single-instance lock held by the copy a developer is using.
function Test-AppStarts {
    param(
        [Parameter(Mandatory)] [scriptblock] $Start,
        [Parameter(Mandatory)] [string] $Describe
    )

    $process = & $Start
    try {
        Start-Sleep -Seconds 8
        $process.Refresh()
        if ($process.HasExited) {
            throw "$Describe exited during startup with code $($process.ExitCode)"
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
}

$checkRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('desknote-package-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null

# `--data <folder>` as one quoted argument string. Start-Process joins an array with spaces and
# quotes nothing, so a temp folder under a user name with a space in it (C:\Users\Jane Doe) would
# reach the app as two arguments, the first of which is somebody else's folder.
function Get-DataArguments {
    param([Parameter(Mandatory)] [string] $Name)

    return '--data "{0}"' -f (Join-Path $checkRoot $Name)
}

try {
    $appPath = Join-Path $outputDirectory 'DeskNote.exe'
    Test-AppStarts -Describe 'Packaged DeskNote' -Start {
        Start-Process -FilePath $appPath -ArgumentList (Get-DataArguments 'data-folder') `
            -WorkingDirectory $outputDirectory -PassThru
    }

    $compressionAttempts = 5
    $compressionLevel = if ([enum]::GetNames([System.IO.Compression.CompressionLevel]) -contains 'SmallestSize') {
        'SmallestSize'
    }
    else {
        'Optimal'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    for ($attempt = 1; $attempt -le $compressionAttempts; $attempt++) {
        try {
            if (Test-Path -LiteralPath $archivePath) {
                Remove-Item -LiteralPath $archivePath -Force
            }

            # Not Compress-Archive: Windows PowerShell 5.1's writes backslashes into the entry
            # names and a folder entry for each directory, which is a zip the launcher below has to
            # guess its way through. This one is the same on every PowerShell.
            [System.IO.Compression.ZipFile]::CreateFromDirectory(
                $outputDirectory, $archivePath, $compressionLevel, $false)
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

    # The one file. It carries the zip above inside itself and unpacks it on the first run (see
    # tools/Launcher), so what somebody is given is a single exe that needs nothing installed.
    $payloadId = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.Substring(0, 16)
    $launcherOutput = Join-Path $checkRoot 'launcher'
    & dotnet build (Join-Path $repositoryRoot 'tools\Launcher\Launcher.csproj') `
        --configuration Release `
        --output $launcherOutput `
        "-p:PayloadZip=$archivePath" `
        "-p:PayloadId=$payloadId"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build of the launcher failed with exit code $LASTEXITCODE"
    }

    Copy-Item -LiteralPath (Join-Path $launcherOutput 'DeskNote.exe') -Destination $singleFilePath -Force

    # The single file is checked the way a person meets it: run it, let it unpack into a folder
    # of its own, and see the app come up from there.
    $installDirectory = Join-Path $checkRoot 'installed'
    $previousInstallDirectory = $env:DESKNOTE_INSTALL_DIR
    $env:DESKNOTE_INSTALL_DIR = $installDirectory
    try {
        $launcher = Start-Process -FilePath $singleFilePath `
            -ArgumentList (Get-DataArguments 'data-single') -PassThru
        if (-not $launcher.WaitForExit(240000)) {
            Stop-Process -Id $launcher.Id -Force
            throw 'The single-file DeskNote.exe did not finish unpacking within four minutes'
        }

        if ($launcher.ExitCode -ne 0) {
            throw "The single-file DeskNote.exe exited with code $($launcher.ExitCode)"
        }

        $unpackedExe = Join-Path $installDirectory 'DeskNote.exe'
        if (-not (Test-Path -LiteralPath $unpackedExe -PathType Leaf)) {
            throw 'The single-file DeskNote.exe did not unpack the app'
        }

        $started = $null
        for ($wait = 0; $wait -lt 60 -and $null -eq $started; $wait++) {
            $started = Get-Process -Name 'DeskNote' -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -eq $unpackedExe } |
                Select-Object -First 1
            if ($null -eq $started) {
                Start-Sleep -Milliseconds 500
            }
        }

        if ($null -eq $started) {
            throw 'The single-file DeskNote.exe unpacked the app but did not start it'
        }

        Test-AppStarts -Describe 'Single-file DeskNote' -Start { $started }
    }
    finally {
        $env:DESKNOTE_INSTALL_DIR = $previousInstallDirectory
    }
}
finally {
    Remove-Item -LiteralPath $checkRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$archive = Get-Item -LiteralPath $archivePath
$singleFile = Get-Item -LiteralPath $singleFilePath
Write-Host ('Created verified package: {0} ({1:N1} MB)' -f $singleFile.FullName, ($singleFile.Length / 1MB))
Write-Host ('Created verified package: {0} ({1:N1} MB)' -f $archive.FullName, ($archive.Length / 1MB))
