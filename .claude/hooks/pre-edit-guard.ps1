# PreToolUse (Edit|Write|MultiEdit|NotebookEdit) - deterministyczne bramki procesu SDLC.
#   1. Skan sekretow w zapisywanej tresci (wszystkie pliki).
#   2. Recenzent (agent_type = sdlc-reviewer) moze zapisywac wylacznie review.md tematu;
#      nikt inny nie moze zapisac review.md (rozdzial rol autor / recenzent).
#   3. Konfiguracja bramek (.claude/settings*.json, hooks/, agents/, skills/, sdlc.json,
#      .githooks/, tools/) jest tylko dla czlowieka.
#   4. Status "approved" w plan.md i pr.md nadaje wylacznie czlowiek (agent dostaje blokade).
#      Edycja juz zatwierdzonego planu / PR wymaga potwierdzenia czlowieka (ask).
#   5. Pliki spoza dokumentacji wolno edytowac tylko, gdy sa wymienione
#      w plan.md ze statusem "approved" (plan "done" juz niczego nie autoryzuje).
#   6. W planie z "Tryb: fix" testy zacommitowane w HEAD sa zablokowane.
#   7. Dodanie [SlopwatchSuppress] i zmiana wzorca snapshotu (*.verified.*) wymagaja
#      potwierdzenia czlowieka (ask) - to akceptacja wyjatku / nowego wyniku.
#   (.slopwatch/ - baseline narzedzia - nalezy do plikow tylko dla czlowieka, pkt 3.)
# Korzen projektu = korzen repozytorium git edytowanego pliku (dziala tez w worktree).
# Kod wyjscia 2 = blokada. Kazdy nieoczekiwany blad tez konczy sie blokada (fail-closed).
# Komunikaty bez polskich znakow celowo - PowerShell 5.1 nie gwarantuje UTF-8 na stderr.

$ErrorActionPreference = 'Stop'

function Deny([string]$msg) {
    [Console]::Error.WriteLine("[SDLC] $msg")
    exit 2
}

function Ask([string]$msg) {
    $out = @{ hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'ask'
        permissionDecisionReason = "[SDLC] $msg"
    } }
    $out | ConvertTo-Json -Depth 5 -Compress
    exit 0
}

try {
    . (Join-Path $PSScriptRoot 'SdlcCommon.ps1')

    try { [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
    $data = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $inp = $data.tool_input
    $isReviewer = ([string]$data.agent_type) -eq 'sdlc-reviewer'

    $path = if ($inp.file_path) { $inp.file_path } else { $inp.notebook_path }
    if (-not $path) { exit 0 }
    if (-not [System.IO.Path]::IsPathRooted($path)) { $path = Join-Path $data.cwd $path }
    $full = [System.IO.Path]::GetFullPath($path)

    # Tresc przed i po zmianie (dla skanu sekretow i kontroli statusow)
    $newText = ''
    $oldText = ''
    switch ($data.tool_name) {
        'Write' {
            $newText = [string]$inp.content
            if (Test-Path -LiteralPath $full) { $oldText = [System.IO.File]::ReadAllText($full) }
        }
        'Edit' { $newText = [string]$inp.new_string; $oldText = [string]$inp.old_string }
        'MultiEdit' {
            $newText = ($inp.edits | ForEach-Object { $_.new_string }) -join "`n"
            $oldText = ($inp.edits | ForEach-Object { $_.old_string }) -join "`n"
        }
        'NotebookEdit' { $newText = [string]$inp.new_source }
    }

    # --- 1. Sekrety -----------------------------------------------------------
    $secretPatterns = @(
        @{ n = 'klucz prywatny';            p = '-----BEGIN [A-Z ]*PRIVATE KEY-----' }
        @{ n = 'AWS access key';            p = 'AKIA[0-9A-Z]{16}' }
        @{ n = 'token GitHub';              p = '(gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{22,})' }
        @{ n = 'token Slack';               p = 'xox[abprs]-[A-Za-z0-9-]{10,}' }
        @{ n = 'klucz Anthropic';           p = 'sk-ant-[A-Za-z0-9_-]{20,}' }
        @{ n = 'klucz Google API';          p = 'AIza[0-9A-Za-z_-]{35}' }
        @{ n = 'haslo w connection string'; p = '(?i)\b(password|pwd)=[^;"''\s<>{}$]{6,};' }
        @{ n = 'sekret przypisany literalem'; p = '(?i)(password|passwd|secret|api[_-]?key|access[_-]?token|client[_-]?secret)["'']?\s*[:=]\s*["''][^"''\s<>{}$]{8,}["'']' }
    )
    foreach ($s in $secretPatterns) {
        if ($newText -match $s.p) {
            Deny "Zablokowano zapis do '$full': wykryto mozliwy sekret ($($s.n)). Uzyj zmiennej srodowiskowej / Key Vault / user-secrets. Jesli to falszywy alarm, uzyj placeholdera (np. <haslo>) lub popros czlowieka o reczny zapis."
        }
    }

    # --- Ustalenie projektu ---------------------------------------------------
    # Bramki obowiazuja w kazdym repozytorium z docs/sdlc (takze w worktree).
    $projectDir = if ($env:CLAUDE_PROJECT_DIR) { [System.IO.Path]::GetFullPath($env:CLAUDE_PROJECT_DIR).TrimEnd('\', '/') } else { $null }
    $root = Get-RepoRoot $full
    if (-not $root -or -not (Test-Path -LiteralPath (Join-Path $root 'docs/sdlc'))) {
        if ($projectDir -and (Test-Under $full $projectDir)) {
            $root = $projectDir
        } elseif ($isReviewer) {
            Deny "Recenzent sdlc-reviewer moze zapisywac wylacznie docs/sdlc/tematy/<ID>/review.md."
        } else {
            exit 0
        }
    }
    $rel = $full.Substring($root.Length + 1).Replace('\', '/')

    # --- 2. Rozdzial rol: review.md ------------------------------------------
    $isReviewFile = $rel -match '^docs/sdlc/tematy/[^/]+/review\.md$'
    if ($isReviewer) {
        if ($isReviewFile) { exit 0 }
        Deny "Recenzent sdlc-reviewer moze zapisywac wylacznie docs/sdlc/tematy/<ID>/review.md (proba zapisu: '$rel')."
    }
    if ($isReviewFile) {
        Deny "Plik '$rel' zapisuje wylacznie recenzent (/sdlc-review lub tools/Invoke-Review.ps1). Autor zmian nie moze edytowac review - popraw kod i uruchom review ponownie."
    }

    # --- 3. Konfiguracja bramek -----------------------------------------------
    if (Test-HumanOnlyPath $rel) {
        Deny "Plik '$rel' konfiguruje bramki procesu i moze go zmieniac tylko czlowiek (poza sesja agenta)."
    }

    # --- 4. Zatwierdzanie planu i PR ------------------------------------------
    if ($rel -match '^docs/sdlc/tematy/[^/]+/(plan|pr)\.md$') {
        $doc = $Matches[1]
        $fileApproved = (Test-Path -LiteralPath $full) -and (Test-PlanApproved ([System.IO.File]::ReadAllText($full)))
        if ((Test-PlanApproved $newText) -and -not (Test-PlanApproved $oldText) -and -not $fileApproved) {
            Deny "Agent nie moze nadac dokumentowi $doc.md statusu 'approved'. Zatwierdzenie (Status: approved + wpis osoby i daty) wykonuje czlowiek. Przedstaw dokument do akceptacji i zatrzymaj sie."
        }
        if ($fileApproved) {
            Ask "Dokument '$rel' jest zatwierdzony. Zmiana zakresu lub statusu wymaga zgody czlowieka - odstepstwa opisz w dokumencie."
        }
        exit 0
    }

    # --- 5. Zakres z zatwierdzonego planu -------------------------------------
    if (Test-FreePath $rel) { exit 0 }

    $authorizing = @(Get-PlanAuthorization $root $rel)
    if ($authorizing.Count -eq 0) {
        Deny "Plik '$rel' nie jest wymieniony w zadnym zatwierdzonym plan.md (docs/sdlc/tematy/<ID>/plan.md, 'Status: approved'). Dopisz plik do planu tematu i uzyskaj akceptacje czlowieka przed edycja."
    }

    # --- 6. Blokada testow w trybie fix ---------------------------------------
    $isTest = $rel -match '(?i)(^|/)(tests?|__tests__|spec)/|\.Tests?/|Tests?\.cs$|\.(test|spec)\.[a-z]+$|(^|/)test_[^/]+\.py$|_test\.(py|go)$'
    $fixTopics = @($authorizing | Where-Object { $_.Fix })
    if ($isTest -and $fixTopics.Count -gt 0) {
        $tracked = Invoke-Git @('-C', $root, '-c', 'core.quotepath=off', 'ls-tree', '-r', '--name-only', 'HEAD')
        if ($tracked -and ($tracked | Where-Object { $_ -ieq $rel })) {
            Deny "Temat $($fixTopics[0].Topic) jest w trybie fix: test '$rel' jest juz zacommitowany i nie wolno go zmieniac. Napraw kod produkcyjny tak, aby test przeszedl. Jesli test jest bledny - zatrzymaj sie i zglos to czlowiekowi."
        }
    }

    # --- 7a. Wyciszenie slopwatch ([SlopwatchSuppress]) akceptuje czlowiek ----
    # Atrybut ukrywa wykrycie (np. wylaczony test) przed bramka - agent nie moze sam go dodac.
    $suppressRx = 'SlopwatchSuppress'
    $before = if (Test-Path -LiteralPath $full) { [System.IO.File]::ReadAllText($full) } else { '' }
    $after = switch ($data.tool_name) {
        'Write'     { [string]$inp.content }
        'Edit'      { if ($inp.old_string) { $before.Replace([string]$inp.old_string, [string]$inp.new_string) } else { $before + [string]$inp.new_string } }
        'MultiEdit' { $t = $before; foreach ($e in $inp.edits) { if ($e.old_string) { $t = $t.Replace([string]$e.old_string, [string]$e.new_string) } }; $t }
        default     { $before + $newText }
    }
    if ([regex]::Matches($after, $suppressRx).Count -gt [regex]::Matches($before, $suppressRx).Count) {
        Ask "Zmiana w '$rel' dodaje [SlopwatchSuppress] - wyciszenie wykrycia slopwatch (np. wylaczonego testu, pustego catch). Wyjatek akceptuje czlowiek; agent powinien naprawic przyczyne."
    }

    # --- 7b. Wzorce snapshotow (Verify *.verified.*) akceptuje czlowiek -------
    if ($rel -match '(?i)\.verified\.[^/]+$') {
        Ask "Plik '$rel' to zatwierdzony wzorzec snapshotu. Zmiana wzorca = akceptacja nowego wyniku - potwierdza ja czlowiek po obejrzeniu roznicy (.received vs .verified)."
    }

    exit 0
}
catch {
    Deny "Blad hooka bramek SDLC - operacja zablokowana (fail-closed): $($_.Exception.Message)"
}
