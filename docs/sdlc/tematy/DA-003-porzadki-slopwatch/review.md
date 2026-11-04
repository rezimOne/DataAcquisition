# Review: DA-003 — Porzadki po slopwatch (SW003, SW004)

Status: no-blocking-findings. Commit: cc6fda7. Baza: 85b87a3. Recenzent: sdlc-reviewer. Data: 2026-11-04.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review DA-003` lub `tools/Invoke-Review.ps1 -Id DA-003`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

Brak uwag w tej iteracji (P1: 0, P2: 0, P3: 0).

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|

Sprawdzone scenariusze, które nie dały uwagi:

- `Worker/MTConnect/MTConnectSynchronizer.cs:42-46` — log `Debug` w `catch ... when (stoppingToken.IsCancellationRequested)`: komunikat stały, bez danych agenta; `RunAsync` nadal kończy się `RanToCompletion`. Timeout `HttpClient` (anulowanie niezwiązane ze `stoppingToken`) dalej trafia do `StepAsync` jako błąd przejściowy (filtr `when` w liniach 58 i 42 bez zmian) — zgodnie ze spec pkt 3.
- `Worker.Tests/TestWaits.cs:10-11` — `new TaskCompletionSource().Task.WaitAsync(ct)`: przy już anulowanym tokenie zwraca od razu zadanie anulowane; po anulowaniu rejestracja tokenu jest zwalniana, TCS nie trzyma zasobów. Semantyka identyczna z `Task.Delay(Timeout.Infinite, ct)` (w tym rzucany `TaskCanceledException`, który `HttpClient` zamienia na timeout) — test `CurrentAsync_AgentDoesNotAnswer_TimesOutAfterRequestTimeout` przechodzi.
- `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs:352` i `:396-402` — sygnał `FirstTimerCreated` ustawiany po `base.CreateTimer`; wyścig „anulowanie między utworzeniem timera a rejestracją tokenu w `Task.Delay`” nie powoduje zawieszenia, bo rejestracja na już anulowanym tokenie wywołuje callback synchronicznie. Gdyby pierwszy timer pochodził z backoffu po błędzie cyklu, asercja „brak `Warning`” wykryłaby to — test nie przechodzi „przypadkiem”.
- `Worker.Tests/WorkerTests.cs:87` — `AddFilter("DataAcquisition", LogLevel.Debug)` dotyczy tylko hosta testowego; konfiguracja produkcyjna bez zmian. Odstępstwo opisane w planie (pkt 2).
- `TaskCreationOptions.RunContinuationsAsynchronously` dodane do `requestStarted` — kontynuacja testu nie wykonuje się już synchronicznie wewnątrz fałszywego agenta; zgodne ze skillem concurrency.

## Zastosowane skille

| Skill | Dla plików / obszaru | Uwagi |
|---|---|---|
| `dotnet-skills:csharp-coding-standards` | wszystkie zmienione `*.cs` | załadowany; brak naruszeń (async z `CancellationToken`, brak `.Result`/`.Wait()`) |
| `dotnet-skills:csharp-nullable-reference-types` | wszystkie zmienione `*.cs` | załadowany; brak nowych `!`, `object? state` w `CreateTimer` zgodne z sygnaturą bazową |
| `dotnet-skills:csharp-concurrency-patterns` | `MTConnectSynchronizer.cs`, `TestWaits.cs`, testy synchronizatora i hosta | załadowany; synchronizacja przez TCS zamiast odpytywania i opóźnień, bez locków; zgodne |
| `dotnet-skills:slopwatch` | cała zmiana | załadowany; usunięte SW003 (catch nie jest już pusty) i 4× SW004; brak nowych wzorców slop (`Skip`, `#pragma`, `NoWarn`, `SlopwatchSuppress`) — sprawdzone wyszukiwaniem w `*.cs` |

Ograniczenie: recenzent nie ma powłoki — slopwatch, build i testy nie były uruchamiane przez recenzenta; ocena opiera się na diffie, kodzie i dowodach w `verification.md`.

## Zgodność z planem

Wszystkie 5 zmienionych plików spoza `docs/` jest w tabeli planu (`pr.md`: „W planie? tak” dla każdego): `Worker/MTConnect/MTConnectSynchronizer.cs`, `Worker.Tests/TestWaits.cs`, `Worker.Tests/WorkerTests.cs`, `Worker.Tests/MTConnect/MTConnectClientTests.cs`, `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs`. Tryb `feature` — uzasadniony w planie (zmiana zacommitowanych testów). Odstępstwa (kolejność kroków 3→1, `AddFilter` zamiast `SetMinimumLevel`, sufiks `…AndLogsStop` w nazwach testów, nazwa `TimerSignalingTimeProvider` zamiast rozbudowy `TimerCountingTimeProvider`) są zgodne z intencją spec; pierwsze trzy opisane w sekcji *Odstępstwa*, zmiana nazwy klasy pomocniczej mieści się w opisie kroku 1. Brak zmian w `.slopwatch/`, `.config/`, `.claude/`, `tools/`.

## Pokrycie kryteriów akceptacji

| AC | Test / dowód w verification.md | Ocena recenzenta |
|---|---|---|
| AC-01 | build / test (55 + 82) / format z kodami 0 dla `6346869` | pokryte |
| AC-02 | slopwatch `--no-baseline`: przed 5 znalezisk (kod 1), po 0 (kod 0); `SlopwatchSuppress` = 0 | pokryte; recenzent potwierdził brak `SlopwatchSuppress` i `Task.Delay` w testach wyszukiwaniem |
| AC-03 | `RunAsync_StoppedWhileWaiting_CompletesWithoutWarningsAndLogsStop`, `RunAsync_StoppedDuringRequest_CompletesWithoutWarningsAndLogsStop`, `WorkerTests.StopAsync_WhileRequestInProgress_CompletesWithoutWarningsOrErrors`; przebieg czerwony 2/55 przed zmianą synchronizatora | pokryte (oba scenariusze zatrzymania: w oczekiwaniu i w żądaniu; asercja braku `Warning`/`Error` i obecności `Debug`) |
| AC-04 | grep `Task.Delay` w `Worker.Tests`/`Api.Tests` = 0; 20/20 przebiegów `dotnet test` | pokryte; jedyne `Task.Delay` w repo to kod produkcyjny `MTConnectSynchronizer.cs:38` (z `TimeProvider`) |

## Dowody weryfikacji

`verification.md` dotyczy commita `6346869` — ostatniego commita zmieniającego kod; późniejsze commity (`9b9c9b5`, `cc6fda7`) zmieniają wyłącznie `docs/`, więc dowody są aktualne dla przejrzanego `cc6fda7`. Dowody zawierają rzeczywiste outputy z kodami wyjścia (bez potoku dla kodów), stan przed/po slopwatch oraz przebieg czerwony test-first. Pominięcia (slopwatch z baseline, realna 1 s w teście timeoutu `HttpClient`, brak weryfikacji logu `Debug` na żywym workerze) są jawnie opisane i zasadne — baseline tworzy człowiek po merge.

## Historia iteracji

| Iteracja | Commit | Data | Wynik |
|---|---|---|---|
| 1 | cc6fda7 | 2026-11-04 | no-blocking-findings: P1 0, P2 0, P3 0 |
