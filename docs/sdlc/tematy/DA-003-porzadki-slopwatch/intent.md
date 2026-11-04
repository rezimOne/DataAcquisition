# Intent: DA-003 — Porzadki po slopwatch (SW003, SW004)

ID: DA-003. Status: ready. Data: 2026-11-04. Odpowiedzialny: nog_programista2. Baza: 85b87a3.

## Problem i dowód

**Fakt**: aktualizacja frameworka SDLC (PR #3) dodała lokalne narzędzie slopwatch 0.4.2 i bramkę w `tools/Test-TopicReady.ps1`, aktywną dopiero po utworzeniu `.slopwatch/baseline.json` (robi to człowiek: `slopwatch init`). Analiza obecnego kodu bez baseline (`dotnet tool run slopwatch analyze --no-baseline`, kod wyjścia 1):

| Kod | Miejsce | Znalezisko |
|---|---|---|
| SW003 (error) | `Worker/MTConnect/MTConnectSynchronizer.cs:42` | Pusty `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` w `RunAsync` — tylko komentarz „Host is stopping.” |
| SW004 (warning) | `Worker.Tests/WorkerTests.cs:55` | `Task.Delay(Timeout.Infinite, ct)` — fałszywy agent „wisi” do anulowania |
| SW004 (warning) | `Worker.Tests/MTConnect/MTConnectClientTests.cs:109` | `Task.Delay(Timeout.Infinite, ct)` — handler HTTP „wisi” do timeoutu |
| SW004 (warning) | `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs:367` | `Task.Delay(Timeout.Infinite, ct)` — fałszywy agent „wisi” do anulowania |
| SW004 (warning) | `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs:387` | `Task.Delay(10)` w pętli `WaitUntilAsync` — odpytywanie, czy synchronizator utworzył timer |

**Hipoteza / ryzyko**: jeśli człowiek uruchomi `slopwatch init` teraz, te 5 znalezisk trafi do baseline jako zaakceptowane i bramka nie wykryje ich powtórzeń. Pusty `catch` ukrywa moment zatrzymania pętli w logach; odpytywanie co 10 ms to potencjalne źródło niestabilnych testów na wolnej maszynie CI.

## Oczekiwany rezultat

Kod repozytorium nie ma znalezisk slopwatch, więc baseline utworzony po tym temacie jest pusty, a bramka slopwatch od razu wykrywa każdy nowy przypadek. Zachowanie workera się nie zmienia; testy nie zależą od czasu rzeczywistego.

## Użytkownicy, systemy i ograniczenia

- **Dotknięci użytkownicy / role**: deweloperzy i recenzent (bramka slopwatch), operator czytający logi workera.
- **Dotknięte systemy / integracje**: `Worker/MTConnect/MTConnectSynchronizer.cs`, testy w `Worker.Tests/`. API, `Contracts/`, `Api.Tests/` — bez zmian (brak znalezisk).
- **Ograniczenia**: bez `[SlopwatchSuppress]` (reguła z `CLAUDE.md` — wykrycia naprawiamy w kodzie); bez zmian `.slopwatch/`, `.config/dotnet-tools.json`, `.claude/` (zmienia człowiek); zachowanie AC-07 z DA-002 (zatrzymanie bez wyjątku i bez `Warning`/`Error`) bez zmian.

## Zakres i poza zakresem

- **W zakresie**:
  - SW003: obsłużenie zatrzymania pętli w `RunAsync` zamiast pustego `catch`,
  - SW004: zastąpienie `Task.Delay` w testach synchronizacją bez czasu (np. `TaskCompletionSource`, oczekiwanie na anulowanie, sygnał „timer utworzony” z `FakeTimeProvider`),
  - potwierdzenie stabilności testów workera wielokrotnym uruchomieniem.
- **Poza zakresem**:
  - utworzenie `.slopwatch/baseline.json` (`slopwatch init`) — robi człowiek po merge,
  - inne zmiany zachowania workera lub API, wysyłka do API (kolejny temat),
  - poprawka `Test-PlanCovers` (framework, zmienia człowiek).

## Kryteria sukcesu (Acceptance Criteria)

- AC-01: `dotnet build DataAcquisition.slnx`, `dotnet test DataAcquisition.slnx --nologo` i `dotnet format DataAcquisition.slnx --verify-no-changes` kończą się kodem 0.
- AC-02: `dotnet tool run slopwatch analyze --no-baseline` kończy się kodem 0 i raportuje 0 znalezisk; w repozytorium nie ma `[SlopwatchSuppress]`.
- AC-03: Zatrzymanie workera (w trakcie oczekiwania i w trakcie żądania do agenta) nadal kończy się bez wyjątku i bez logów `Warning`/`Error`, a moment zatrzymania jest widoczny w logu (poziom niższy niż `Warning`).
- AC-04: Testy w `Worker.Tests/` nie używają `Task.Delay` ani odpytywania w pętli; cały zestaw testów solucji przechodzi 20 razy z rzędu.

## Pytania i decyzja

| Pytanie | Decyzja | Kto, kiedy |
|---|---|---|
| Jak obsłużyć zatrzymanie w `RunAsync` (SW003)? | Łapanie `OperationCanceledException` przy zatrzymaniu zostaje, ale z logiem `Debug` „MTConnect synchronization stopped”; `RunAsync` dalej kończy się bez wyjątku | Product owner, 2026-11-04 |
| Kto i kiedy tworzy baseline? | Człowiek, po merge DA-003: `dotnet tool run slopwatch init` (oczekiwany pusty) i commit `.slopwatch/baseline.json` | Product owner, 2026-11-04 |

- **Decyzja product ownera**: zaakceptowano — nog_programista2, 2026-11-04. Status tematu: `ready`.
