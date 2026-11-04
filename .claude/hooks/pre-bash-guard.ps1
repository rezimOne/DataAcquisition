# PreToolUse (Bash, if: "Bash(git *)") - agent nie tworzy commitow na main/master.
# Blokuje polecenia git, ktore tworza lub przepisuja commity (commit, merge, cherry-pick,
# revert, am, rebase, pull), gdy biezaca galaz w katalogu polecenia to main/master.
# Praca agenta odbywa sie na galeziach topic/<ID>-<nazwa>; merge robi czlowiek na
# zdalnym repozytorium. Czlowieka (terminal poza agentem) ten hook nie dotyczy.
# Kod wyjscia 2 = blokada; kazdy nieoczekiwany blad = blokada (fail-closed).

$ErrorActionPreference = 'Stop'

function Deny([string]$msg) {
    [Console]::Error.WriteLine("[SDLC] $msg")
    exit 2
}

try {
    . (Join-Path $PSScriptRoot 'SdlcCommon.ps1')

    try { [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
    $data = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $command = [string]$data.tool_input.command
    if (-not $command) { exit 0 }

    # Podzial na polecenia skladowe (&&, ||, ;, |, nowa linia) i wykrycie git <podkomenda>
    $writing = 'commit|merge|cherry-pick|revert|am|rebase|pull'
    $hits = @()
    foreach ($part in ($command -split '&&|\|\||;|\||\r?\n')) {
        $m = [regex]::Match($part, "(?i)(^|\s|\()git(\s+(-C\s+(""[^""]+""|'[^']+'|\S+)|-c\s+\S+|--[\w-]+(=\S+)?))*\s+($writing)\b")
        if ($m.Success) {
            $dir = $null
            $c = [regex]::Match($part, "(?i)\bgit\s+(?:\S+\s+)*?-C\s+(""[^""]+""|'[^']+'|\S+)")
            if ($c.Success) { $dir = $c.Groups[1].Value.Trim('"', "'") }
            $hits += [pscustomobject]@{ Sub = $m.Groups[6].Value.ToLower(); Dir = $dir }
        }
    }
    if ($hits.Count -eq 0) { exit 0 }

    foreach ($h in $hits) {
        $dir = if ($h.Dir) {
            if ([System.IO.Path]::IsPathRooted($h.Dir)) { $h.Dir } else { Join-Path $data.cwd $h.Dir }
        } else { $data.cwd }
        $branch = Invoke-Git @('-C', $dir, 'rev-parse', '--abbrev-ref', 'HEAD')
        if (-not $branch) { continue }   # poza repozytorium git
        $branch = ([string]$branch).Trim()
        if ($branch -match '^(main|master)$') {
            Deny "Agent nie moze wykonac 'git $($h.Sub)' na galezi '$branch'. Pracuj na galezi tematu (git switch -c topic/<ID>-<nazwa> lub tools/New-Topic.ps1). Merge do $branch robi czlowiek na zdalnym repozytorium."
        }
    }
    exit 0
}
catch {
    Deny "Blad hooka bramek SDLC (Bash) - polecenie zablokowane (fail-closed): $($_.Exception.Message)"
}
