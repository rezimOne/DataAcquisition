[CmdletBinding()]
param(
    # ID tematu; domyślnie z nazwy bieżącej gałęzi topic/<ID>-<nazwa>
    [Parameter(Position = 0)]
    [string]$Id = ""
)

# Odświeża sekcję automatyczną w docs/sdlc/tematy/<ID>/pr.md (commity, pliki vs plan,
# statystyka, AC vs verification) i zapisuje pełny diff dla recenzenta poza repozytorium
# śledzonym przez git (.git/sdlc/<ID>.diff). Nie zmienia statusu PR ani sekcji opisowych.

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $projectRoot ".claude\hooks\SdlcCommon.ps1")

$root = Get-RepoRoot $projectRoot
if (-not $root) { Write-Error "Projekt nie jest repozytorium git - lokalny PR wymaga gita."; exit 1 }
$config = Get-SdlcConfig $root

$branch = ([string](Invoke-Git @('-C', $root, 'rev-parse', '--abbrev-ref', 'HEAD'))).Trim()
if (-not $Id) {
    if ($branch -notmatch '^topic/(.+)$') { Write-Error "Podaj -Id albo przełącz się na gałąź topic/<ID>-<nazwa> (bieżąca: $branch)."; exit 1 }
    $topicDir = Join-Path $root "docs/sdlc/tematy/$($Matches[1])"
} else {
    $topicDir = Get-TopicDir $root $Id
}
if (-not $topicDir -or -not (Test-Path -LiteralPath $topicDir)) { Write-Error "Nie znaleziono katalogu tematu ($Id / $branch)."; exit 1 }
$topicName = Split-Path -Leaf $topicDir
$topicRel = "docs/sdlc/tematy/$topicName"
$prFile = Join-Path $topicDir "pr.md"
if (-not (Test-Path -LiteralPath $prFile)) { Write-Error "Brak $topicRel/pr.md - skopiuj szablon docs/sdlc/szablony/temat/pr.md."; exit 1 }

$base = $config.baseBranch
if (-not (Invoke-Git @('-C', $root, 'rev-parse', '--verify', '--quiet', "$base^{commit}"))) {
    Write-Error "Gałąź bazowa '$base' nie istnieje (baseBranch w .claude/sdlc.json)."; exit 1
}
$mergeBase = ([string](Invoke-Git @('-C', $root, 'merge-base', $base, 'HEAD'))).Trim()
$head = ([string](Invoke-Git @('-C', $root, 'rev-parse', 'HEAD'))).Trim()
$headShort = $head.Substring(0, 7)
$baseShort = $mergeBase.Substring(0, 7)

# Pliki review/PR tematu nie są przedmiotem review
$excludes = @(":(exclude)$topicRel/review.md", ":(exclude)$topicRel/pr.md")

$commits = @(Invoke-Git @('-C', $root, '-c', 'core.quotepath=off', 'log', '--format=%h %s', "$mergeBase..HEAD") | Where-Object { $_ })
$changes = @(Invoke-Git (@('-C', $root, '-c', 'core.quotepath=off', 'diff', '--name-status', $mergeBase, 'HEAD', '--') + $excludes) | Where-Object { $_ })
$stat = @(Invoke-Git (@('-C', $root, 'diff', '--shortstat', $mergeBase, 'HEAD', '--') + $excludes) | Where-Object { $_ })
$dirty = @(Invoke-Git @('-C', $root, 'status', '--porcelain') | Where-Object { $_ })
$diffRel = ".claude/sdlc-tmp/$topicName.diff"

$planFile = Join-Path $topicDir "plan.md"
$planText = if (Test-Path -LiteralPath $planFile) { [System.IO.File]::ReadAllText($planFile) } else { "" }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("<!-- auto:start -->")
[void]$sb.AppendLine("Wygenerowano: $((Get-Date).ToString('yyyy-MM-dd HH:mm')). Gałąź: ``$branch``. Baza: ``$base`` (merge-base ``$baseShort``). **HEAD: ``$headShort``**.")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("Diff do review: ``git diff $baseShort $headShort`` — pełna treść w ``$diffRel`` (plik lokalny, nieśledzony przez git).")
if ($dirty.Count -gt 0) {
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("> **Uwaga:** niezacommitowane zmiany ($($dirty.Count) plików) nie są częścią tego PR.")
}

[void]$sb.AppendLine("")
[void]$sb.AppendLine("### Commity ($($commits.Count))")
[void]$sb.AppendLine("")
if ($commits.Count -eq 0) { [void]$sb.AppendLine("_Brak commitów względem bazy._") }
else { foreach ($c in $commits) { [void]$sb.AppendLine("- ``$($c.Substring(0, 7))``$($c.Substring(7))") } }

[void]$sb.AppendLine("")
[void]$sb.AppendLine("### Zmienione pliki")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| Zmiana | Plik | W planie? |")
[void]$sb.AppendLine("|---|---|---|")
$outOfPlan = 0
foreach ($line in $changes) {
    if (-not $line) { continue }
    $cols = $line -split "`t"
    $kind = $cols[0].Substring(0, 1)
    $file = $cols[-1]
    $inPlan = if ($file -match '^docs/' -or $file -match '^(CLAUDE|AGENTS|REVIEW|README)\.md$') { "dokumentacja" }
              elseif (Test-PlanCovers $planText $file) { "tak" }
              else { $outOfPlan++; "**NIE**" }
    [void]$sb.AppendLine("| $kind | ``$file`` | $inPlan |")
}
if ($stat.Count -gt 0) { [void]$sb.AppendLine(""); [void]$sb.AppendLine("Statystyka: $(([string]$stat[0]).Trim())") }
if ($outOfPlan -gt 0) { [void]$sb.AppendLine(""); [void]$sb.AppendLine("> **$outOfPlan plik(ów) spoza planu** — dopisz do plan.md (odstępstwo) albo usuń z gałęzi.") }

# AC z intent.md zestawione z wynikami z verification.md
$intentFile = Join-Path $topicDir "intent.md"
$verFile = Join-Path $topicDir "verification.md"
$acs = @()
if (Test-Path -LiteralPath $intentFile) {
    foreach ($m in [regex]::Matches([System.IO.File]::ReadAllText($intentFile), '(?m)^\s*-\s*(AC-\d+):\s*(.+)$')) {
        $acs += [pscustomobject]@{ Id = $m.Groups[1].Value; Text = $m.Groups[2].Value.Trim() }
    }
}
$results = @{}
if (Test-Path -LiteralPath $verFile) {
    foreach ($row in ([System.IO.File]::ReadAllLines($verFile) | Where-Object { $_ -match '^\|\s*AC-\d+\s*\|' })) {
        $cells = $row -split '\|'
        if ($cells.Count -ge 6) { $results[$cells[1].Trim()] = $cells[4].Trim() }
    }
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("### Kryteria akceptacji")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| AC | Kryterium (intent.md) | Wynik (verification.md) |")
[void]$sb.AppendLine("|---|---|---|")
foreach ($ac in $acs) {
    $r = if ($results.ContainsKey($ac.Id)) { $results[$ac.Id] } else { "**brak wiersza**" }
    [void]$sb.AppendLine("| $($ac.Id) | $($ac.Text.Replace('|', '/')) | $r |")
}
[void]$sb.Append("<!-- auto:end -->")

$pr = [System.IO.File]::ReadAllText($prFile)
$rx = '(?s)<!-- auto:start -->.*?<!-- auto:end -->'
if ($pr -notmatch $rx) { Write-Error "W pr.md brak znaczników <!-- auto:start --> / <!-- auto:end -->."; exit 1 }
$pr = [regex]::Replace($pr, $rx, { param($m) $sb.ToString() })
[System.IO.File]::WriteAllText($prFile, $pr, $utf8NoBom)

# Pełny diff dla recenzenta (recenzent nie ma dostępu do Basha). Katalog .claude/sdlc-tmp/
# jest dopisywany do info/exclude repozytorium, więc nie trafia do commitów ani nie
# brudzi drzewa roboczego. git zapisuje plik sam (bez konwersji kodowania).
$commonDir = ([string](Invoke-Git @('-C', $root, 'rev-parse', '--git-common-dir'))).Trim()
if (-not [System.IO.Path]::IsPathRooted($commonDir)) { $commonDir = Join-Path $root $commonDir }
$excludeFile = Join-Path $commonDir "info\exclude"
New-Item -ItemType Directory -Path (Split-Path -Parent $excludeFile) -Force | Out-Null
$excludeText = if (Test-Path -LiteralPath $excludeFile) { [System.IO.File]::ReadAllText($excludeFile) } else { "" }
if ($excludeText -notmatch '(?m)^/\.claude/sdlc-tmp/\s*$') {
    [System.IO.File]::AppendAllText($excludeFile, "`n/.claude/sdlc-tmp/`n", $utf8NoBom)
}
$diffFile = Join-Path $root $diffRel
New-Item -ItemType Directory -Path (Split-Path -Parent $diffFile) -Force | Out-Null
Invoke-Git (@('-C', $root, 'diff', '--output', $diffFile, $mergeBase, 'HEAD', '--') + $excludes) | Out-Null
Write-Host "Diff dla recenzenta: $diffRel" -ForegroundColor Gray

Write-Host "Zaktualizowano: $topicRel/pr.md (HEAD $headShort, commity: $($commits.Count), pliki spoza planu: $outOfPlan)" -ForegroundColor Green
