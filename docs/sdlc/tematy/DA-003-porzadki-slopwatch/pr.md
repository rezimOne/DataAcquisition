# Pull Request: DA-003 — Porzadki po slopwatch (SW003, SW004)

Status: draft. Gałąź: topic/DA-003-porzadki-slopwatch. Gałąź bazowa: main. Autor: nog_programista2. Źródła: [intent](intent.md), [plan](plan.md), [verification](verification.md), [review](review.md).

> Cykl statusu: draft → approved. Status approved nadaje wyłącznie człowiek po przeczytaniu `review.md` (hook blokuje agenta) — w tym samym commicie ustawia `Status: done` w `plan.md`. Push gałęzi przepuszcza dopiero `tools/Test-TopicReady.ps1` (hook git pre-push). Merge wykonuje człowiek na zdalnym repozytorium.

## Opis zmiany

Nowy framework SDLC (PR #3) dodał slopwatch; analiza obecnego kodu wykazała 5 znalezisk w workerze z DA-002 (1× SW003 pusty `catch`, 4× SW004 `Task.Delay` w testach). Zmiana usuwa je w kodzie (bez `[SlopwatchSuppress]`), żeby baseline tworzony przez człowieka po merge był pusty, a bramka slopwatch od razu wykrywała nowe przypadki. Zachowanie workera bez zmian poza dodatkowym logiem `Debug` przy zatrzymaniu.

## Najważniejsze zmiany

- `Worker/MTConnect/MTConnectSynchronizer.cs` — SW003: w `RunAsync` obsługa zatrzymania (`OperationCanceledException` przy anulowanym `stoppingToken`) loguje `Debug` „MTConnect synchronization stopped.” zamiast pustego `catch`; `RunAsync` nadal kończy się bez wyjątku (decyzja PO w `intent.md`).
- `Worker.Tests/TestWaits.cs` (nowy) — `UntilCanceledAsync(ct)`: `new TaskCompletionSource().Task.WaitAsync(ct)` — „wiszenie do anulowania” bez `Task.Delay`.
- `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs` — `TimerSignalingTimeProvider` z `FirstTimerCreated` (TCS ustawiany w `CreateTimer`) zamiast odpytywania licznika co 10 ms; fałszywy agent czeka przez `TestWaits`; nowe asercje logu `Debug` (`…AndLogsStop`).
- `Worker.Tests/WorkerTests.cs` — fałszywy agent przez `TestWaits`; asercja logu `Debug` w teście hosta; `AddFilter("DataAcquisition", LogLevel.Debug)` w `BuildHost`.
- `Worker.Tests/MTConnect/MTConnectClientTests.cs` — `StubHandler.Hanging` przez `TestWaits`.

## Ryzyka i wpływ

- **Produkcja**: tylko nowy log `Debug` przy zatrzymaniu workera — brak wpływu na logi `Information`+, kontrakty, konfigurację.
- **Testy**: zmienia się wyłącznie sposób czekania; asercje zachowania bez zmian. Bezpiecznik 5 s (`WaitAsync`) zapobiega zawieszeniu, gdyby sygnał nie nadszedł.
- **Po merge**: człowiek uruchamia `dotnet tool run slopwatch init` (oczekiwany pusty baseline) i commituje `.slopwatch/baseline.json` — od tego momentu bramka slopwatch jest aktywna.

## Jak testowano

- `dotnet build` (0 ostrzeżeń), `dotnet test --nologo` (Worker.Tests 55/55, Api.Tests 82/82), `dotnet format --verify-no-changes` — kod 0 dla `6346869`.
- `dotnet tool run slopwatch analyze --no-baseline`: przed 5 znalezisk (kod 1), po **0** (kod 0); `SlopwatchSuppress` w repo: 0.
- Test-first: nowe asercje logu `Debug` czerwone (2/55) przed zmianą synchronizatora, zielone po.
- `Task.Delay` w testach: 0 wystąpień; **20/20** przebiegów `dotnet test` z rzędu zielonych.
- **Nie testowano**: slopwatch z baseline (`--fail-on warning`) — baseline tworzy człowiek; log `Debug` na żywym workerze (tylko testy).

## Uwagi dla recenzenta

- **Odstępstwa** (`plan.md`): (1) krok 3 (`TestWaits.cs`) przed krokiem 1, bo testy z niego korzystają; (2) `AddFilter("DataAcquisition", Debug)` zamiast `SetMinimumLevel(Debug)` w `WorkerTests.BuildHost` — reguła `Default` z `appsettings.json` workera (kopiowanego do katalogu testów) wygrywała z `SetMinimumLevel` i log `Debug` nie był zbierany; (3) nazwy testów `RunAsync_Stopped…` z sufiksem `…AndLogsStop`.
- **Wymagane skille** z planu: `csharp-coding-standards`, `csharp-nullable-reference-types`, `csharp-concurrency-patterns` (anulowanie, `TaskCompletionSource` z `RunContinuationsAsynchronously`), `slopwatch` (weryfikacja: 0 znalezisk).
- `CurrentAsync_AgentDoesNotAnswer_TimesOutAfterRequestTimeout` nadal czeka realną 1 s — mierzy prawdziwy `HttpClient.Timeout`; slopwatch go nie zgłasza, poza zakresem.

## Dane automatyczne

Sekcję generuje `tools/New-PullRequest.ps1` — nie edytuj ręcznie.

<!-- auto:start -->
Wygenerowano: 2026-11-04 22:45. Gałąź: `topic/DA-003-porzadki-slopwatch`. Baza: `main` (merge-base `85b87a3`). **HEAD: `9b9c9b5`**.

Diff do review: `git diff 85b87a3 9b9c9b5` — pełna treść w `.claude/sdlc-tmp/DA-003-porzadki-slopwatch.diff` (plik lokalny, nieśledzony przez git).

### Commity (5)

- `9b9c9b5` DA-003: verification.md (AC-01..AC-04 dla 6346869)
- `6346869` DA-003: SW003 log Debug przy zatrzymaniu synchronizacji; SW004 testy bez Task.Delay
- `37cb18c` DA-003: plan approved (akceptacja czlowieka)
- `e9061fe` DA-003: spec approved
- `3a5e8e4` DA-003: intent (ready), spec (draft), plan (draft)

### Zmienione pliki

| Zmiana | Plik | W planie? |
|---|---|---|
| M | `Worker.Tests/MTConnect/MTConnectClientTests.cs` | tak |
| M | `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs` | tak |
| A | `Worker.Tests/TestWaits.cs` | tak |
| M | `Worker.Tests/WorkerTests.cs` | tak |
| M | `Worker/MTConnect/MTConnectSynchronizer.cs` | tak |
| M | `docs/sdlc/01-plan/BACKLOG.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-003-porzadki-slopwatch/intent.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-003-porzadki-slopwatch/plan.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-003-porzadki-slopwatch/release.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-003-porzadki-slopwatch/spec.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-003-porzadki-slopwatch/verification.md` | dokumentacja |

Statystyka: 11 files changed, 354 insertions(+), 26 deletions(-)

### Kryteria akceptacji

| AC | Kryterium (intent.md) | Wynik (verification.md) |
|---|---|---|
| AC-01 | `dotnet build DataAcquisition.slnx`, `dotnet test DataAcquisition.slnx --nologo` i `dotnet format DataAcquisition.slnx --verify-no-changes` kończą się kodem 0. | PASS |
| AC-02 | `dotnet tool run slopwatch analyze --no-baseline` kończy się kodem 0 i raportuje 0 znalezisk; w repozytorium nie ma `[SlopwatchSuppress]`. | PASS |
| AC-03 | Zatrzymanie workera (w trakcie oczekiwania i w trakcie żądania do agenta) nadal kończy się bez wyjątku i bez logów `Warning`/`Error`, a moment zatrzymania jest widoczny w logu (poziom niższy niż `Warning`). | PASS |
| AC-04 | Testy w `Worker.Tests/` nie używają `Task.Delay` ani odpytywania w pętli; cały zestaw testów solucji przechodzi 20 razy z rzędu. | PASS |
<!-- auto:end -->

## Decyzja

- **Akceptacja do wypchnięcia**: <osoba, data — approved / changes-requested> — wpisuje człowiek po przeczytaniu `review.md`; razem z nią `plan.md` → `Status: done` (plan przestaje autoryzować edycję plików), a temat w `intent.md` / `BACKLOG.md` → `verified`.
- **Zdalny PR / merge**: <link lub numer PR na zdalnym repozytorium, data merge — uzupełnia człowiek>.
