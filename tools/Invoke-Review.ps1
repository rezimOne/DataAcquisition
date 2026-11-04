[CmdletBinding()]
param(
    # ID tematu; domyślnie z bieżącej gałęzi topic/<ID>-<nazwa>
    [Parameter(Position = 0)]
    [string]$Id = "",

    # Limit tur recenzenta
    [int]$MaxTurns = 40
)

# Lokalne AI review bez otwierania sesji Claude Code (odpowiednik AI review w CI).
# 1) odświeża pr.md i diff (tools/New-PullRequest.ps1), 2) uruchamia `claude -p` jako
# agent sdlc-reviewer (Read/Grep/Glob/Write, zapis tylko review.md - pilnuje hook),
# 3) sprawdza, że review.md dotyczy bieżącego HEAD. Kod 0 = review wykonane.

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $projectRoot ".claude\hooks\SdlcCommon.ps1")

if (-not (Get-Command claude -ErrorAction SilentlyContinue)) { Write-Error "Nie znaleziono polecenia 'claude' (Claude Code CLI) w PATH."; exit 1 }

& (Join-Path $PSScriptRoot "New-PullRequest.ps1") -Id $Id
if ($LASTEXITCODE -ne 0) { exit 1 }

$root = Get-RepoRoot $projectRoot
$branch = ([string](Invoke-Git @('-C', $root, 'rev-parse', '--abbrev-ref', 'HEAD'))).Trim()
$topicDir = if ($Id) { Get-TopicDir $root $Id } elseif ($branch -match '^topic/(.+)$') { Join-Path $root "docs/sdlc/tematy/$($Matches[1])" } else { $null }
$topicName = Split-Path -Leaf $topicDir
$headShort = ([string](Invoke-Git @('-C', $root, 'rev-parse', '--short=7', 'HEAD'))).Trim()

$prompt = "Przeprowadz review tematu $topicName (katalog docs/sdlc/tematy/$topicName/). Przegladany commit: $headShort. Postepuj zgodnie ze swoimi instrukcjami i zapisz review.md."
Write-Host "Uruchamiam recenzenta sdlc-reviewer dla $topicName (HEAD $headShort)..." -ForegroundColor Cyan

Push-Location -LiteralPath $root
try {
    & claude -p $prompt --agent sdlc-reviewer --permission-mode acceptEdits --max-turns $MaxTurns
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($code -ne 0) { Write-Error "claude -p zakończył się kodem $code."; exit 1 }

$reviewFile = Join-Path $topicDir "review.md"
$review = if (Test-Path -LiteralPath $reviewFile) { [System.IO.File]::ReadAllText($reviewFile) } else { "" }
$status = Get-HeaderField $review 'Status'
$commit = Get-HeaderField $review 'Commit'
if (-not $commit -or -not $headShort.StartsWith($commit.Substring(0, [Math]::Min(7, $commit.Length)), [StringComparison]::OrdinalIgnoreCase) -or $status -eq 'not-run') {
    Write-Error "review.md nie został zaktualizowany dla HEAD $headShort (Status: $status, Commit: $commit)."
    exit 1
}

$rows = @(($review -split "`r?`n") | Where-Object { $_ -match '^\|\s*R-\d+\s*\|' })
$open = @($rows | Where-Object { $_ -match '\|\s*otwarta\s*\|\s*$' })
$openBlocking = @($open | Where-Object { $_ -match '^\|\s*R-\d+\s*\|\s*P[12]\s*\|' })
$color = if ($openBlocking.Count -gt 0) { 'Yellow' } else { 'Green' }
Write-Host ""
Write-Host "Review: $status (commit $commit). Otwarte uwagi: $($open.Count), w tym P1/P2: $($openBlocking.Count)." -ForegroundColor $color
Write-Host "Plik: docs/sdlc/tematy/$topicName/review.md" -ForegroundColor Gray
exit 0
