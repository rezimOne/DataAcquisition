# Wspolne funkcje bramek SDLC - dot-source: . (Join-Path $PSScriptRoot 'SdlcCommon.ps1')
# Uzywane przez hooki Claude Code (.claude/hooks/*.ps1) i skrypty tools/*.ps1.
# Plik zmienia tylko czlowiek (chroniony hookiem i regulami deny).

$script:SdlcApprovedRx = '(?im)^\s*Status:\s*approved\b'

# Wywolanie git bez wyjatku przy stderr (PowerShell 5.1 + ErrorActionPreference=Stop
# zamienia stderr programu w blad). Zwraca $null, gdy git zakonczy sie bledem.
function Invoke-Git([string[]]$gitArgs) {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return $null }
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & git @gitArgs 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return $out
    } finally {
        $ErrorActionPreference = $prev
    }
}

# Korzen repozytorium git dla sciezki (najblizszy istniejacy katalog nadrzedny)
function Get-RepoRoot([string]$fullPath) {
    $dir = if (Test-Path -LiteralPath $fullPath -PathType Container) { $fullPath } else { Split-Path -Parent $fullPath }
    while ($dir -and -not (Test-Path -LiteralPath $dir -PathType Container)) { $dir = Split-Path -Parent $dir }
    if (-not $dir) { return $null }
    $top = Invoke-Git @('-C', $dir, 'rev-parse', '--show-toplevel')
    if (-not $top) { return $null }
    return [System.IO.Path]::GetFullPath(([string]$top).Trim()).TrimEnd('\', '/')
}

function Test-Under([string]$path, [string]$root) {
    return $path.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

# Tresc Markdown bez blokow kodu (``` lub ~~~, takze niezamknietych - do konca pliku, jak w
# CommonMark). Przyklady komend i kodu w planie nie sa zakresem planu ani jego statusem,
# a ich backticki nie moga przesuwac parowania sciezek w dalszej czesci dokumentu.
function Remove-MdCodeBlocks([string]$text) {
    $t = $text.Replace("`r`n", "`n")
    $rx = '(?ms)^[ \t]{0,3}(?<f>`{3,}|~{3,})[^\n]*\n.*?(?:^[ \t]{0,3}\k<f>[`~]*[ \t]*$|\z)'
    return [regex]::Replace($t, $rx, '')
}

# Sciezki w kodzie inline (`...`, ``...`` itd.) poza blokami kodu - wg CommonMark: ciag n
# backtickow zamyka sie ciagiem dokladnie n backtickow.
function Get-MdInlineCode([string]$text) {
    $rx = '(?<!`)(?<t>`+)(?!`)(?<c>.+?)(?<!`)\k<t>(?!`)'
    foreach ($m in [regex]::Matches((Remove-MdCodeBlocks $text), $rx)) {
        $c = $m.Groups['c'].Value
        if ($c.Length -ge 2 -and $c.StartsWith(' ') -and $c.EndsWith(' ')) { $c = $c.Substring(1, $c.Length - 2) }
        $c
    }
}

function Test-PlanApproved([string]$planText) {
    return (Remove-MdCodeBlocks $planText) -match $script:SdlcApprovedRx
}

# Czy token z planu (sciezka w backtickach) obejmuje plik $rel (sciezka wzgledna, '/')
function Test-PlanToken([string]$token, [string]$rel) {
    $tok = $token.Trim().Replace('\', '/')
    if ($tok.StartsWith('./')) { $tok = $tok.Substring(2) }
    # Wzorce zaczynajace sie od '*' lub '/' autoryzowalyby cale repozytorium
    if ($tok -eq '' -or $tok.StartsWith('*') -or $tok.StartsWith('/')) { return $false }
    return ($tok -ieq $rel) -or
           ($tok.EndsWith('/') -and $rel.StartsWith($tok, [StringComparison]::OrdinalIgnoreCase)) -or
           ($tok.Contains('*') -and ($rel -like $tok))
}

# Czy plan (tresc) wymienia plik $rel - w kodzie inline poza blokami kodu
function Test-PlanCovers([string]$planText, [string]$rel) {
    foreach ($tok in (Get-MdInlineCode $planText)) {
        if (Test-PlanToken $tok $rel) { return $true }
    }
    return $false
}

# Tematy z zatwierdzonym planem, ktore obejmuja plik $rel.
# Zwraca obiekty { Topic; Fix } (Fix = plan w trybie fix). Pomija PRZYKLAD-*.
function Get-PlanAuthorization([string]$root, [string]$rel) {
    $result = @()
    $plansDir = Join-Path $root 'docs/sdlc/tematy'
    if (-not (Test-Path -LiteralPath $plansDir)) { return $result }
    foreach ($topic in (Get-ChildItem -LiteralPath $plansDir -Directory | Where-Object { $_.Name -notlike 'PRZYKLAD-*' })) {
        $planFile = Join-Path $topic.FullName 'plan.md'
        if (-not (Test-Path -LiteralPath $planFile)) { continue }
        $plan = [System.IO.File]::ReadAllText($planFile)
        if (-not (Test-PlanApproved $plan)) { continue }
        if (Test-PlanCovers $plan $rel) {
            $result += [pscustomobject]@{ Topic = $topic.Name; Fix = ($plan -match '(?i)\bTryb:\s*fix\b') }
        }
    }
    return $result
}

# Pliki, ktorych zmiana nie wymaga planu: dokumentacja i glowne pliki instrukcji.
# Jedna definicja dla hooka edycji, New-PullRequest i Test-TopicReady - wszystko inne
# (takze .claude/ poza plikami tylko dla czlowieka) musi byc wymienione w plan.md.
function Test-FreePath([string]$rel) {
    return $rel -match '(?i)^(docs/|(CLAUDE|AGENTS|REVIEW|README)\.md$)'
}

# Pliki konfigurujace bramki - zmienia je wylacznie czlowiek (poza sesja agenta)
function Test-HumanOnlyPath([string]$rel) {
    # .slopwatch/ = baseline narzedzia slopwatch (akceptacja "dlugu" nalezy do czlowieka)
    # dotnet-tools.json (w .config/ lub w korzeniu) = manifest narzedzi lokalnych .NET (wersje narzedzi bramek)
    return $rel -match '(?i)^(\.claude/(settings[^/]*\.json$|hooks/|agents/|skills/|sdlc\.json$)|\.githooks/|tools/|\.slopwatch/|(\.config/)?dotnet-tools\.json$)'
}

# Manifest narzedzi lokalnych .NET: dotnet szuka .config/dotnet-tools.json, potem dotnet-tools.json
# w korzeniu (nowsze SDK tworza go w korzeniu). Zwraca istniejaca sciezke lub $null.
function Get-DotnetToolsManifest([string]$root) {
    foreach ($candidate in @('.config/dotnet-tools.json', 'dotnet-tools.json')) {
        $p = Join-Path $root $candidate
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

# Uruchomienie programu z lista argumentow bez wyjatku przy stderr; zwraca kod wyjscia.
# Wyjscie programu idzie na konsole (Out-Host), chyba ze -Quiet.
function Invoke-External([string]$exe, [string[]]$argv, [string]$workDir, [switch]$Quiet) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    Push-Location -LiteralPath $workDir
    try {
        if ($Quiet) { & $exe @argv 2>&1 | Out-Null } else { & $exe @argv 2>&1 | Out-Host }
        return $LASTEXITCODE
    } finally {
        Pop-Location
        $ErrorActionPreference = $prev
    }
}

# Sposob wywolania slopwatch w projekcie: narzedzie lokalne z manifestu
# .config/dotnet-tools.json (preferowane, wersja przypieta w repo) albo globalne.
# Zwraca { Exe; Prefix; Local } lub $null.
function Get-SlopwatchInvocation([string]$root) {
    $manifest = Get-DotnetToolsManifest $root
    if ($manifest -and ([System.IO.File]::ReadAllText($manifest) -match '"slopwatch"')) {
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            return [pscustomobject]@{ Exe = 'dotnet'; Prefix = @('tool', 'run', 'slopwatch'); Local = $true }
        }
    }
    if (Get-Command slopwatch -ErrorAction SilentlyContinue) {
        return [pscustomobject]@{ Exe = 'slopwatch'; Prefix = @(); Local = $false }
    }
    return $null
}

# Konfiguracja projektu SDLC (.claude/sdlc.json) z wartosciami domyslnymi
function Get-SdlcConfig([string]$root) {
    $cfg = [pscustomobject]@{ baseBranch = 'main'; testCommand = @() }
    $file = Join-Path $root '.claude/sdlc.json'
    if (Test-Path -LiteralPath $file) {
        $json = [System.IO.File]::ReadAllText($file) | ConvertFrom-Json
        if ($json.baseBranch) { $cfg.baseBranch = [string]$json.baseBranch }
        if ($json.testCommand) { $cfg.testCommand = @($json.testCommand | ForEach-Object { [string]$_ }) }
    }
    return $cfg
}

# Katalog tematu dla ID (docs/sdlc/tematy/<ID>-<nazwa>) lub $null
function Get-TopicDir([string]$root, [string]$id) {
    $dir = Join-Path $root 'docs/sdlc/tematy'
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    $hit = Get-ChildItem -LiteralPath $dir -Directory | Where-Object { $_.Name -ieq $id -or $_.Name -like "$id-*" } | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return $null
}

# Wartosc pola z naglowka dokumentu, np. "Commit: abc123." -> abc123
function Get-HeaderField([string]$text, [string]$name) {
    $m = [regex]::Match($text, "(?im)\b$([regex]::Escape($name)):\s*([^\s.]+)")
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}
