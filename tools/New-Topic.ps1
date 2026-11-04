[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0, HelpMessage = "Identyfikator tematu, np. PROJ-001")]
    [ValidatePattern('^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$')]
    [string]$Id,

    [Parameter(Mandatory = $true, Position = 1, HelpMessage = "Krótka nazwa tematu (slug), np. logowanie-oauth")]
    [ValidatePattern('^[a-z0-9]+(-[a-z0-9]+)*$')]
    [string]$Name,

    [Parameter(Mandatory = $false)]
    [string]$Title = "",

    [Parameter(Mandatory = $false)]
    [string]$Author = $env:USERNAME,

    # Nie twórz gałęzi topic/<ID>-<nazwa> (domyślnie tworzona, gdy projekt jest repozytorium git)
    [switch]$NoBranch
)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
$sdlcRoot = Join-Path $projectRoot "docs\sdlc"
$templateDir = Join-Path $sdlcRoot "szablony\temat"
$targetDir = Join-Path $sdlcRoot "tematy\$Id-$Name"

if (-not (Test-Path $templateDir)) {
    Write-Error "Nie znaleziono katalogu szablonu: $templateDir"
    exit 1
}

$existing = Get-ChildItem -Path (Join-Path $sdlcRoot "tematy") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ieq $Id -or $_.Name -like "$Id-*" }
if ($existing) {
    Write-Error "Temat o ID $Id już istnieje: $($existing[0].FullName)"
    exit 1
}

. (Join-Path $projectRoot ".claude\hooks\SdlcCommon.ps1")
$config = Get-SdlcConfig $projectRoot
$branchName = "topic/$Id-$Name"

# Pobranie SHA bieżącego commita z repozytorium projektu (jeśli istnieje)
$commitSha = "brak-git"
$gitOutput = Invoke-Git @('-C', $projectRoot, 'rev-parse', '--short', 'HEAD')
if ($gitOutput) { $commitSha = ([string]$gitOutput).Trim() }
$isRepo = [bool](Invoke-Git @('-C', $projectRoot, 'rev-parse', '--git-dir'))

# Gałąź tematu - PR to porównanie tej gałęzi z gałęzią bazową (baseBranch w .claude/sdlc.json)
if (-not $NoBranch) {
    if (-not $isRepo) {
        Write-Warning "Projekt nie jest repozytorium git - pomijam tworzenie gałęzi $branchName."
    } elseif (Invoke-Git @('-C', $projectRoot, 'rev-parse', '--verify', '--quiet', "refs/heads/$branchName")) {
        Write-Error "Gałąź $branchName już istnieje."
        exit 1
    } elseif ($commitSha -eq "brak-git") {
        Write-Warning "Repozytorium nie ma jeszcze żadnego commita - zrób pierwszy commit na $($config.baseBranch), potem: git switch -c $branchName"
    } else {
        & git -C $projectRoot switch -c $branchName
        if ($LASTEXITCODE -ne 0) { Write-Error "Nie udało się utworzyć gałęzi $branchName."; exit 1 }
        Write-Host "  -> Utworzono gałąź: $branchName (baza: $($config.baseBranch))" -ForegroundColor Gray
    }
}

$today = (Get-Date).ToString("yyyy-MM-dd")
$topicTitle = if ($Title) { $Title } else {
    $words = $Name.Replace("-", " ")
    $words.Substring(0, 1).ToUpper() + $words.Substring(1)
}

Write-Host "Tworzenie nowego tematu $Id ($topicTitle)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

Get-ChildItem -Path $templateDir -Filter *.md | ForEach-Object {
    $targetFile = Join-Path $targetDir $_.Name
    $content = [System.IO.File]::ReadAllText($_.FullName)

    $content = $content.Replace("<ID — tytuł>", "$Id — $topicTitle")
    $content = $content.Replace("<ID>", $Id)
    $content = $content.Replace("<tytuł>", $topicTitle)
    $content = $content.Replace("<data>", $today)
    $content = $content.Replace("<osoba>", $Author)
    $content = $content.Replace("<commit>", $commitSha)
    $content = $content.Replace("<SHA>", $commitSha)
    $content = $content.Replace("<gałąź>", $branchName)
    $content = $content.Replace("<baza>", $config.baseBranch)

    [System.IO.File]::WriteAllText($targetFile, $content, $utf8NoBom)
    Write-Host "  -> Utworzono: $($_.Name)" -ForegroundColor Gray
}

# Dodanie wiersza na końcu tabeli tematów w BACKLOG.md (nie na końcu pliku)
$backlogPath = Join-Path $sdlcRoot "01-plan\BACKLOG.md"
if (Test-Path $backlogPath) {
    $backlogLine = "| [$Id](../tematy/$Id-$Name/intent.md) | draft | P2 | M | $topicTitle | $Author | - |"
    $lines = [System.Collections.Generic.List[string]]([System.IO.File]::ReadAllLines($backlogPath))

    $header = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\|\s*ID\s*\|') { $header = $i; break }
    }

    if ($header -ge 0) {
        $insertAt = $header + 1
        while ($insertAt -lt $lines.Count -and $lines[$insertAt].TrimStart().StartsWith('|')) { $insertAt++ }
        $lines.Insert($insertAt, $backlogLine)
        [System.IO.File]::WriteAllLines($backlogPath, $lines, $utf8NoBom)
        Write-Host "  -> Dodano wpis do: 01-plan/BACKLOG.md" -ForegroundColor Gray
    } else {
        Write-Warning "Nie znaleziono tabeli tematów w BACKLOG.md - dodaj wpis ręcznie: $backlogLine"
    }
}

Write-Host "Gotowe! Temat zainicjalizowany w:" -ForegroundColor Green
Write-Host "  $targetDir" -ForegroundColor Yellow
Write-Host "Kolejny krok: Otwórz intent.md i zdefiniuj problem oraz kryteria AC." -ForegroundColor Cyan
