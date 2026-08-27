<#
.SYNOPSIS
  Сборка portable Windows-релиза PDF Compress через Velopack.

.DESCRIPTION
  1. publish self-contained win-x64
  2. vpk pack  -> создаёт Setup.exe, portable .zip и пакеты в .\Releases
  3. (опционально) vpk upload github -> публикует релиз в GitHub Releases

.PARAMETER Version
  Версия релиза (SemVer), напр. 0.1.0. По умолчанию берётся из csproj.

.PARAMETER Upload
  Если указан — загрузить релиз в GitHub Releases (нужен -Token или $env:GITHUB_TOKEN).

.PARAMETER Token
  GitHub PAT с правом на запись релизов. По умолчанию — $env:GITHUB_TOKEN.

.EXAMPLE
  pwsh build/pack-win.ps1 -Version 0.1.0
  pwsh build/pack-win.ps1 -Version 0.1.0 -Upload
#>
param(
    [string]$Version = "",
    [switch]$Upload,
    [string]$Token = $env:GITHUB_TOKEN,
    # Markdown-файл с заметками релиза. По умолчанию build/release-notes-<Version>.md, если есть.
    [string]$ReleaseNotes = "",
    # Параметры signtool.exe для подписи (проброс в vpk --signParams), напр.:
    #   '/fd sha256 /tr http://timestamp.digicert.com /td sha256 /a'                                (из хранилища Windows)
    #   '/fd sha256 /f C:\path\cert.pfx /p PASSWORD /tr http://timestamp.digicert.com /td sha256'   (из PFX)
    [string]$SignParams = ""
)

$ErrorActionPreference = "Stop"
$repoRoot   = Split-Path -Parent $PSScriptRoot
$proj       = Join-Path $repoRoot "src\PdfCompress.Desktop\PdfCompress.Desktop.csproj"
$publishDir = Join-Path $repoRoot "publish"
$releaseDir = Join-Path $repoRoot "Releases"
$repoUrl    = "https://github.com/OlBaskov-rec/pdfCompress"

# Title без пробела = имя стаба-лаунчера portable: «PdfCompress.exe» (совпадает с приложением,
# чтобы в папке не было двух похожих exe). Отображаемое имя в самой программе — «PDF Compress».
$packId    = "PdfCompress"
$mainExe   = "PdfCompress.exe"
$title     = "PdfCompress"
$rid       = "win-x64"
$iconPath  = Join-Path $repoRoot "src\PdfCompress.Desktop\Assets\app.ico"

# Версия из csproj, если не задана явно
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$csproj = Get-Content $proj
    $Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "Не удалось определить версию. Укажите -Version." }
}
Write-Host "==> Версия релиза: $Version" -ForegroundColor Cyan

# Заметки релиза: явный путь или соглашение build/release-notes-<Version>.md
if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $candidate = Join-Path $PSScriptRoot "release-notes-$Version.md"
    if (Test-Path $candidate) { $ReleaseNotes = $candidate }
}
if ($ReleaseNotes) { Write-Host "==> Заметки релиза: $ReleaseNotes" -ForegroundColor Cyan }

# Инструмент vpk (из манифеста .config/dotnet-tools.json)
Push-Location $repoRoot
try {
    dotnet tool restore

    Write-Host "==> publish self-contained $rid" -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish $proj -c Release -r $rid --self-contained true -o $publishDir

    Write-Host "==> vpk pack" -ForegroundColor Cyan
    $packArgs = @(
        "--packId", $packId,
        "--packVersion", $Version,
        "--packDir", $publishDir,
        "--mainExe", $mainExe,
        "--packTitle", $title,
        "--runtime", $rid,
        "--outputDir", $releaseDir
    )
    if (Test-Path $iconPath) { $packArgs += @("--icon", $iconPath) }
    if ($ReleaseNotes) { $packArgs += @("--releaseNotes", $ReleaseNotes) }
    if ($SignParams)   { $packArgs += @("--signParams", $SignParams) }
    dotnet vpk pack @packArgs

    if ($Upload) {
        if ([string]::IsNullOrWhiteSpace($Token)) {
            throw "Для -Upload нужен GitHub-токен: параметр -Token или переменная окружения GITHUB_TOKEN."
        }
        Write-Host "==> vpk upload github (publish)" -ForegroundColor Cyan
        dotnet vpk upload github `
            --outputDir $releaseDir `
            --repoUrl $repoUrl `
            --token $Token `
            --publish true `
            --releaseName "v$Version" `
            --tag "v$Version" `
            --merge true
    }

    Write-Host "==> Готово. Артефакты в: $releaseDir" -ForegroundColor Green
    Get-ChildItem $releaseDir | Select-Object Name, Length
}
finally {
    Pop-Location
}
