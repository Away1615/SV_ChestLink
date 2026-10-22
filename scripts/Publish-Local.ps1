param(
    [string]$ModsPath,
    [string]$ManifestLocale = 'default'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $projectRoot 'src\ChestLink\ChestLink.csproj'
$sourcePath = Join-Path $projectRoot 'src\ChestLink'
$buildPath = Join-Path $sourcePath 'bin\Release\net6.0'
$packagePath = Join-Path $projectRoot 'dist\ChestLink'

if ($ManifestLocale -notmatch '^(default|[a-z]{2}(?:-[A-Za-z]{2})?)$' -or
    -not (Test-Path -LiteralPath (Join-Path $sourcePath "i18n\$ManifestLocale.json") -PathType Leaf)) {
    throw "No translation file for manifest locale: $ManifestLocale"
}
if ($ModsPath) {
    if (-not (Test-Path -LiteralPath $ModsPath -PathType Container)) {
        throw "Mods folder not found: $ModsPath"
    }
    if (Get-Process Stardew* -ErrorAction SilentlyContinue) {
        throw 'Close Stardew Valley before installing ChestLink.'
    }
}

dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'ChestLink build failed.'
}

function Write-LocalizedManifest([string]$destinationPath, [string]$locale) {
    if ($locale -notmatch '^(default|[a-z]{2}(?:-[A-Za-z]{2})?)$') {
        throw "Invalid manifest locale: $locale"
    }
    $translationPath = Join-Path $sourcePath "i18n\$locale.json"
    if (-not (Test-Path -LiteralPath $translationPath -PathType Leaf)) {
        throw "No translation file for locale: $locale"
    }
    $description = (Get-Content -Raw -Encoding UTF8 -LiteralPath $translationPath | ConvertFrom-Json).'mod.description'
    if ([string]::IsNullOrWhiteSpace($description)) {
        throw "Missing mod.description in $translationPath"
    }
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $sourcePath 'manifest.json') | ConvertFrom-Json
    $manifest.Description = $description
    $json = ($manifest | ConvertTo-Json -Depth 16) + "`n"
    [IO.File]::WriteAllText((Join-Path $destinationPath 'manifest.json'), $json, [Text.UTF8Encoding]::new($false))
}

function Copy-ChestLinkPackage([string]$destinationPath, [string]$locale) {
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    foreach ($name in @('ChestLink.dll', 'ChestLink.deps.json', 'ChestLink.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $buildPath $name) -Destination (Join-Path $destinationPath $name) -Force
    }
    Write-LocalizedManifest $destinationPath $locale
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

Copy-ChestLinkPackage $packagePath 'default'

if ($ModsPath) {
    Copy-ChestLinkPackage (Join-Path $ModsPath 'ChestLink') $ManifestLocale
}
