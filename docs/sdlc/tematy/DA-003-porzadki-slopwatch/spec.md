# Spec: DA-003 — Porzadki po slopwatch (SW003, SW004)

Status: approved. Odpowiedzialny: nog_programista2. Data: 2026-11-04. Źródło: [intent](intent.md).

## Zachowanie

### SW003 — zatrzymanie pętli synchronizacji

`MTConnectSynchronizer.RunAsync(stoppingToken)`:

1. Przy anulowaniu `stoppingToken` (w trakcie `Task.Delay` między cyklami albo w trakcie żądania do agenta) pętla kończy się, a `RunAsync` **zwraca się normalnie** (zadanie `RanToCompletion`, bez wyjątku) — jak dotąd (DA-002, AC-07).
2. Nowe: w bloku obsługi zatrzymania log `Debug` „MTConnect synchronization stopped.” (bez danych z agenta). Brak logów `Warning`/`Error` przy zatrzymaniu.
3. `OperationCanceledException` niezwiązany z `stoppingToken` (np. timeout `HttpClient`) nadal obsługuje `StepAsync` jako błąd przejściowy — bez zmian.

Żadnych innych zmian zachowania workera.

### SW004 — testy bez zależności od czasu rzeczywistego

| Miejsce | Obecnie | Docelowo |
|---|---|---|
| `Worker.Tests/WorkerTests.cs` (fałszywy agent w `StopAsync_WhileRequestInProgress_…`) | `await Task.Delay(Timeout.Infinite, ct)` | Oczekiwanie na anulowanie bez `Task.Delay`: wspólny pomocnik testowy, który kończy się wyjątkiem anulowania dopiero, gdy `ct` zostanie anulowany (np. `TaskCompletionSource` + `WaitAsync(ct)`) |
| `Worker.Tests/MTConnect/MTConnectClientTests.cs` (`StubHandler.Hanging`) | `await Task.Delay(Timeout.Infinite, ct)` | Ten sam pomocnik — handler „wisi” do anulowania przez `HttpClient.Timeout` |
| `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs` (`RunAsync_StoppedDuringRequest_…`) | `await Task.Delay(Timeout.Infinite, ct)` | Ten sam pomocnik |
| `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs` (`WaitUntilAsync` w `RunAsync_StoppedWhileWaiting_…`) | Pętla `Task.Delay(10)` sprawdzająca licznik timerów | Sygnał z `TimerCountingTimeProvider`: zadanie kończone przy utworzeniu timera (`TaskCompletionSource`), oczekiwane z limitem `WaitAsync(TimeSpan, ct)` jako zabezpieczenie przed zawieszeniem |

Asercje testów pozostają te same (zmienia się tylko sposób czekania); w `RunAsync_Stopped…` dodatkowo sprawdzany log `Debug` o zatrzymaniu.

Ograniczenie świadome: `WaitAsync(TimeSpan, ct)` z limitem (5 s) zostaje jako bezpiecznik — nie jest opóźnieniem, tylko maksymalnym czasem oczekiwania na sygnał.

## Dane i kontrakty

Brak zmian kontraktów, modeli, konfiguracji i formatów. Nowy komunikat logu: kategoria `DataAcquisition.Worker.MTConnect.MTConnectSynchronizer`, poziom `Debug`, treść „MTConnect synchronization stopped.”.

## Dostęp i interfejsy

Bez zmian. Log `Debug` pojawi się tylko wtedy, gdy konfiguracja logowania workera dopuszcza poziom `Debug` dla tej kategorii — to informacja diagnostyczna, nie operacyjna.

## Kryteria odbioru

| AC | Przypadek testowy / Wejście | Oczekiwany wynik |
|---|---|---|
| AC-01 | `dotnet build DataAcquisition.slnx`; `dotnet test DataAcquisition.slnx --nologo`; `dotnet format DataAcquisition.slnx --verify-no-changes` | Kod 0 |
| AC-02 | `dotnet tool run slopwatch analyze --no-baseline`; wyszukanie `SlopwatchSuppress` w repozytorium | Kod 0, „0 issue(s)”; brak `SlopwatchSuppress` |
| AC-03 | `RunAsync_StoppedWhileWaiting_…`, `RunAsync_StoppedDuringRequest_…`, `WorkerTests.StopAsync_WhileRequestInProgress_…` | `RunAsync`/`StopAsync` bez wyjątku; brak `Warning`/`Error`; jest log `Debug` „MTConnect synchronization stopped.” |
| AC-04 | Wyszukanie `Task.Delay` w `Worker.Tests/` i `Api.Tests/`; `dotnet test DataAcquisition.slnx --nologo` 20 razy z rzędu | Brak `Task.Delay` w testach; 20/20 przebiegów z kodem 0 |

## Ryzyka i rozstrzygnięcia

- **Zawieszenie testu** przy błędzie w sygnale „timer utworzony” — ograniczone limitem `WaitAsync(5 s)`; test kończy się wtedy błędem, nie wisi.
- **Zmiana zachowania produkcyjnego** — tylko dodatkowy log `Debug`; brak wpływu na logi `Information`+.
- **Alternatywa odrzucona**: `[SlopwatchSuppress]` z uzasadnieniem — sprzeczne z regułą „wykrycia naprawiaj w kodzie”; przepuszczenie anulowania z `RunAsync` — zmienia kontrakt (decyzja PO w intent).
