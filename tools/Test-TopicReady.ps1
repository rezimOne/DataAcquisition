[CmdletBinding()]
param(
    # ID tematu; domyślnie z gałęzi (-Branch lub bieżącej) topic/<ID>-<nazwa>
    [string]$Id = "",

    # Gałąź do sprawdzenia (przekazuje hook git pre-push); musi być aktualnie wybrana
    [string]$Branch = ""
)

# Bramki "gotowe do wypchnięcia" dla gałęzi tematu. Wynik: GO (kod 0) / NO-GO (kod 1).
# Uruchamiany ręcznie lub przez hook git .githooks/pre-push. Niczego nie zmienia
# (poza uruchomieniem testów z .claude/sdlc.json). Merge wykonuje człowiek na zdalnym repo.

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
. (Join-Path $projectRoot ".claude\hooks\SdlcCommon.ps1")

$checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$name, [bool]$ok, [string]$detail) {
    $checks.Add([pscustomobject]@{ Name = $name; Ok = $ok; Detail = $detail })
}
function Write-Result {
    Write-Host ""
    Write-Host "Bramki SDLC - $script:label" -ForegroundColor Cyan
    foreach ($c in $checks) {
        $mark = if ($c.Ok) { "[ OK ]" } else { "[ NO ]" }
        $color = if ($c.Ok) { "Green" } else { "Red" }
        Write-Host ("  {0} {1}: {2}" -f $mark, $c.Name, $c.Detail) -ForegroundColor $color
    }
    $failed = @($checks | Where-Object { -not $_.Ok }).Count
    if ($failed -eq 0) {
        Write-Host "WYNIK: GO - gałąź gotowa do wypchnięcia. Merge wykonaj na zdalnym repozytorium." -ForegroundColor Green
        exit 0
    }
    Write-Host "WYNIK: NO-GO ($failed niespełnionych warunków)." -ForegroundColor Red
    exit 1
}
$script:label = "temat ?"

$root = Get-RepoRoot $projectRoot
if (-not $root) { Add-Check "Repozytorium" $false "projekt nie jest repozytorium git"; Write-Result }
$config = Get-SdlcConfig $root

# --- Gałąź i temat -------------------------------------------------------------
$current = ([string](Invoke-Git @('-C', $root, 'rev-parse', '--abbrev-ref', 'HEAD'))).Trim()
if (-not $Branch) { $Branch = $current }
if ($Id) {
    $topicDir = Get-TopicDir $root $Id
} elseif ($Branch -match '^topic/(.+)$') {
    $topicDir = Join-Path $root "docs/sdlc/tematy/$($Matches[1])"
} else {
    $topicDir = $null
}
if (-not $topicDir -or -not (Test-Path -LiteralPath $topicDir)) {
    Add-Check "Temat" $false "nie znaleziono katalogu tematu dla gałęzi '$Branch' (oczekiwane topic/<ID>-<nazwa> = docs/sdlc/tematy/<ID>-<nazwa>)"
    Write-Result
}
$topicName = Split-Path -Leaf $topicDir
$topicRel = "docs/sdlc/tematy/$topicName"
$script:label = "$topicName (gałąź $Branch)"

Add-Check "Gałąź" ($current -eq $Branch) $(if ($current -eq $Branch) { "sprawdzana gałąź jest wybrana" } else { "wypychana gałąź '$Branch' różni się od bieżącej '$current' - przełącz się na nią" })
$dirty = @(Invoke-Git @('-C', $root, 'status', '--porcelain') | Where-Object { $_ })
Add-Check "Drzewo robocze" ($dirty.Count -eq 0) $(if ($dirty.Count -eq 0) { "czyste" } else { "$($dirty.Count) niezacommitowanych zmian" })

$head = ([string](Invoke-Git @('-C', $root, 'rev-parse', 'HEAD'))).Trim()
$base = $config.baseBranch
$mergeBase = $null
if (Invoke-Git @('-C', $root, 'rev-parse', '--verify', '--quiet', "$base^{commit}")) {
    $mergeBase = ([string](Invoke-Git @('-C', $root, 'merge-base', $base, 'HEAD'))).Trim()
}

function Read-TopicDoc([string]$name) {
    $f = Join-Path $topicDir $name
    if (Test-Path -LiteralPath $f) { return [System.IO.File]::ReadAllText($f) }
    return $null
}

# Czy od commita $sha do HEAD zmieniły się wyłącznie dokumenty tematu
function Test-OnlyTopicDocsSince([string]$sha) {
    if (-not $sha -or $sha -match '^<') { return "brak SHA w nagłówku" }
    $full = Invoke-Git @('-C', $root, 'rev-parse', '--verify', '--quiet', "$sha^{commit}")
    if (-not $full) { return "commit $sha nie istnieje" }
    $full = ([string]$full).Trim()
    Invoke-Git @('-C', $root, 'merge-base', '--is-ancestor', $full, 'HEAD') | Out-Null
    if ($LASTEXITCODE -ne 0) { return "commit $sha nie jest przodkiem HEAD" }
    $changed = @(Invoke-Git @('-C', $root, '-c', 'core.quotepath=off', 'diff', '--name-only', $full, 'HEAD') | Where-Object { $_ })
    $other = @($changed | Where-Object { -not $_.StartsWith("$topicRel/") })
    if ($other.Count -gt 0) { return "po commicie $sha zmieniono kod: $(($other | Select-Object -First 3) -join ', ')$(if ($other.Count -gt 3) { ' ...' })" }
    return $null
}

# --- Plan i zakres -------------------------------------------------------------
$plan = Read-TopicDoc 'plan.md'
$planStatus = if ($plan) { Get-HeaderField $plan 'Status' } else { $null }
$planOk = $planStatus -in @('approved', 'done')
$scopeDetail = "status planu: $planStatus"
if ($planOk -and $mergeBase) {
    $files = @(Invoke-Git @('-C', $root, '-c', 'core.quotepath=off', 'diff', '--name-only', $mergeBase, 'HEAD') | Where-Object { $_ })
    $outside = @($files | Where-Object { $_ -notmatch '^docs/' -and $_ -notmatch '^(CLAUDE|AGENTS|REVIEW|README)\.md$' -and -not (Test-PlanCovers $plan $_) })
    if ($outside.Count -gt 0) { $planOk = $false; $scopeDetail = "pliki spoza planu: $(($outside | Select-Object -First 3) -join ', ')$(if ($outside.Count -gt 3) { ' ...' })" }
    else { $scopeDetail = "plan $planStatus, $($files.Count) zmienionych plików w zakresie" }
} elseif (-not $mergeBase) {
    $planOk = $false; $scopeDetail = "gałąź bazowa '$base' nie istnieje (baseBranch w .claude/sdlc.json)"
}
Add-Check "Plan i zakres" $planOk $scopeDetail

# --- Weryfikacja -----------------------------------------------------------------
$ver = Read-TopicDoc 'verification.md'
$verStatus = if ($ver) { Get-HeaderField $ver 'Status' } else { $null }
$verProblem = if ($verStatus -ne 'verified') { "status: $verStatus (wymagany verified)" } else { Test-OnlyTopicDocsSince (Get-HeaderField $ver 'Commit') }
Add-Check "Weryfikacja" (-not $verProblem) $(if ($verProblem) { $verProblem } else { "verified, dowody aktualne dla kodu" })

# --- Review ----------------------------------------------------------------------
$rev = Read-TopicDoc 'review.md'
$revStatus = if ($rev) { Get-HeaderField $rev 'Status' } else { $null }
$revProblem = $null
if (-not $rev -or $revStatus -eq 'not-run' -or -not $revStatus) { $revProblem = "review nie zostało wykonane (/sdlc-review lub tools/Invoke-Review.ps1)" }
else {
    $revProblem = Test-OnlyTopicDocsSince (Get-HeaderField $rev 'Commit')
    if ($revProblem) { $revProblem = "review nieaktualne - $revProblem; uruchom review ponownie" }
    else {
        $open = @(($rev -split "`r?`n") | Where-Object { $_ -match '^\|\s*R-\d+\s*\|\s*P[12]\s*\|' -and $_ -match '\|\s*otwarta\s*\|\s*$' })
        if ($open.Count -gt 0) { $revProblem = "$($open.Count) otwartych uwag P1/P2" }
    }
}
Add-Check "Review" (-not $revProblem) $(if ($revProblem) { $revProblem } else { "aktualne, brak otwartych P1/P2 (status: $revStatus)" })

# --- Akceptacja człowieka ---------------------------------------------------------
$pr = Read-TopicDoc 'pr.md'
$prStatus = if ($pr) { Get-HeaderField $pr 'Status' } else { $null }
Add-Check "Akceptacja PR" ($prStatus -eq 'approved') $(if ($prStatus -eq 'approved') { "pr.md: approved" } else { "pr.md: $prStatus - akceptację (Status: approved + osoba i data) wpisuje człowiek" })

# --- Testy -----------------------------------------------------------------------
if ($config.testCommand.Count -eq 0) {
    Add-Check "Testy" $false "brak testCommand w .claude/sdlc.json - skonfiguruj komendę testów"
} elseif (@($checks | Where-Object { -not $_.Ok }).Count -gt 0) {
    Add-Check "Testy" $false "pominięte - najpierw spełnij pozostałe warunki"
} else {
    $exe = $config.testCommand[0]
    $rest = if ($config.testCommand.Count -gt 1) { $config.testCommand[1..($config.testCommand.Count - 1)] } else { @() }
    Write-Host "Uruchamiam testy: $($config.testCommand -join ' ')" -ForegroundColor Gray
    Push-Location -LiteralPath $root
    try {
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & $exe @rest | Out-Host
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prev
        Add-Check "Testy" ($code -eq 0) "$($config.testCommand -join ' ') -> kod $code"
    } catch {
        Add-Check "Testy" $false "nie udało się uruchomić '$exe': $($_.Exception.Message)"
    } finally {
        Pop-Location
    }
}

Write-Result
