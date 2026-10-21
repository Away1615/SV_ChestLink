param(
    [string]$ModsPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $projectRoot 'src\ChestLink\ChestLink.csproj'
$sourcePath = Join-Path $projectRoot 'src\ChestLink'
$buildPath = Join-Path $sourcePath 'bin\Release\net6.0'
$packagePath = Join-Path $projectRoot 'dist\ChestLink'

dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'ChestLink build failed.'
}

function Copy-ChestLinkPackage([string]$destinationPath) {
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    foreach ($name in @('ChestLink.dll', 'ChestLink.deps.json', 'ChestLink.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $buildPath $name) -Destination (Join-Path $destinationPath $name) -Force
    }
    Copy-Item -LiteralPath (Join-Path $sourcePath 'manifest.json') -Destination (Join-Path $destinationPath 'manifest.json') -Force
    foreach ($directory in @('assets', 'i18n')) {
        Copy-Item -Path (Join-Path $sourcePath "$directory\*") -Destination (New-Item -ItemType Directory -Path (Join-Path $destinationPath $directory) -Force).FullName -Recurse -Force
    }

    # A stale PDB causes SMAPI to report SymbolsNotMatchingException on startup.
    # The release package does not include debug symbols.
    $stalePdb = Join-Path $destinationPath 'ChestLink.pdb'
    if (Test-Path -LiteralPath $stalePdb) {
        Remove-Item -LiteralPath $stalePdb
    }

    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $buildPath 'ChestLink.dll')).Hash
    $destinationHash = (Get-FileHash -LiteralPath (Join-Path $destinationPath 'ChestLink.dll')).Hash
    if ($sourceHash -ne $destinationHash) {
        throw "ChestLink.dll verification failed for $destinationPath"
    }
}

Copy-ChestLinkPackage $packagePath

if ($ModsPath) {
    if (-not (Test-Path -LiteralPath $ModsPath -PathType Container)) {
        throw "Mods folder not found: $ModsPath"
    }
    if (Get-Process Stardew* -ErrorAction SilentlyContinue) {
        throw 'Close Stardew Valley before installing ChestLink.'
    }
    Copy-ChestLinkPackage (Join-Path $ModsPath 'ChestLink')
}
