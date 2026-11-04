# Plan: DA-003 — Porzadki po slopwatch (SW003, SW004)

Status: done. Tryb: feature. Baza: 85b87a3. Odpowiedzialny: nog_programista2. Źródło: [spec](spec.md).

> Cykl statusu: draft → approved → done (done ustawia człowiek przy akceptacji PR, przed pushem). Status approved nadaje wyłącznie człowiek (hook blokuje agenta). Tylko przy statusie approved hook pozwala edytować pliki wymienione w tabeli poniżej — ścieżki względem korzenia repozytorium, w backtickach; dozwolony katalog zakończony ukośnikiem lub wzorzec z gwiazdką (nie może zaczynać się od gwiazdki).
> Tryb fix — testy zacommitowane w HEAD są zablokowane do edycji: najpierw commit z czerwonym testem, potem poprawka bez zmiany testu.

## Pliki i kolejność zmian

Tryb `feature` (nie `fix`), bo zmieniamy zacommitowane testy (SW004). Plan bez bloków kodu z potrójnymi backtickami (zob. _Powtarzające się błędy_ w `CLAUDE.md`).

| Krok | Plik / Komponent                                       | Rodzaj zmiany | Opis                                                                                                                                                                                                                                                                                                                      | Zależność |
| ---- | ------------------------------------------------------ | ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| 1    | `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs` | [MODIFY]      | Asercja logu `Debug` „MTConnect synchronization stopped.” w `RunAsync_StoppedWhileWaiting_…` i `RunAsync_StoppedDuringRequest_…` (czerwone przed krokiem 2); sygnał „timer utworzony” z `TimerCountingTimeProvider` zamiast `WaitUntilAsync` z `Task.Delay(10)`; fałszywy agent czeka na anulowanie pomocnikiem z kroku 3 | brak      |
| 2    | `Worker/MTConnect/MTConnectSynchronizer.cs`            | [MODIFY]      | SW003: log `Debug` w obsłudze zatrzymania w `RunAsync` (zamiast pustego `catch`)                                                                                                                                                                                                                                          | Krok 1    |
| 3    | `Worker.Tests/TestWaits.cs`                            | [NEW]         | Pomocnik testowy: oczekiwanie do anulowania tokenu bez `Task.Delay` (`TaskCompletionSource` + `WaitAsync(ct)`)                                                                                                                                                                                                            | brak      |
| 4    | `Worker.Tests/WorkerTests.cs`                          | [MODIFY]      | SW004: fałszywy agent w `StopAsync_WhileRequestInProgress_…` używa pomocnika z kroku 3; asercja logu `Debug` o zatrzymaniu                                                                                                                                                                                                | Kroki 2–3 |
| 5    | `Worker.Tests/MTConnect/MTConnectClientTests.cs`       | [MODIFY]      | SW004: `StubHandler.Hanging` używa pomocnika z kroku 3                                                                                                                                                                                                                                                                    | Krok 3    |

## Wymagane skille

- `dotnet-skills:csharp-coding-standards`, `dotnet-skills:csharp-nullable-reference-types` — wszystkie kroki (kod C#).
- `dotnet-skills:csharp-concurrency-patterns` — kroki 1–5 (anulowanie, `BackgroundService`, `TaskCompletionSource`).
- `dotnet-skills:slopwatch` — weryfikacja całej zmiany (AC-02).

## Ryzyka

| Ryzyko                                                                                                        | Wykrycie / ograniczenie                                                                      |
| ------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| Zmiana sposobu czekania w testach ukrywa realny problem (test przechodzi, bo nie czeka na właściwe zdarzenie) | Asercje bez zmian + przebieg czerwony nowych asercji logu przed krokiem 2; przegląd w review |
| Test zawiesza się, gdy sygnał „timer utworzony” nie nadejdzie                                                 | `WaitAsync` z limitem 5 s — test kończy się błędem, nie wisi                                 |
| Niestabilność testów                                                                                          | 20 przebiegów `dotnet test` z rzędu (AC-04)                                                  |
| Nowe znaleziska slopwatch w zmienionym kodzie (np. inny wzorzec czekania)                                     | `slopwatch analyze --no-baseline` po zmianie (AC-02)                                         |
| Regresja zachowania workera                                                                                   | Pozostałe testy synchronizatora i hosta bez zmian, cały zestaw zielony                       |

## Weryfikacja

Komendy (kod 0 wymagany, output do `verification.md`, kody wyjścia bez potoku):

- `dotnet build DataAcquisition.slnx`
- `dotnet test DataAcquisition.slnx --nologo` (1 raz z pełnym outputem + 20 razy z rzędu, liczba przebiegów z kodem 0)
- `dotnet format DataAcquisition.slnx --verify-no-changes`
- `dotnet tool run slopwatch analyze --no-baseline`
- wyszukanie `Task.Delay` w `Worker.Tests/` i `Api.Tests/` oraz `SlopwatchSuppress` w repozytorium

| AC    | Dowód                                                                                                   |
| ----- | ------------------------------------------------------------------------------------------------------- |
| AC-01 | Output build / test / format                                                                            |
| AC-02 | Output slopwatch („0 issue(s)”, kod 0); wynik wyszukiwania `SlopwatchSuppress`                          |
| AC-03 | Testy `RunAsync_Stopped…` i `StopAsync_WhileRequestInProgress_…` (czerwone przed krokiem 2, zielone po) |
| AC-04 | Wynik wyszukiwania `Task.Delay`; 20/20 przebiegów testów                                                |

## Wydanie i odtworzenie (Rollback)

- Brak migracji, konfiguracji i zmian kontraktów; jedyna zmiana produkcyjna to log `Debug` przy zatrzymaniu workera.
- Po merge człowiek tworzy baseline: `dotnet tool run slopwatch init` (oczekiwany pusty) i commituje `.slopwatch/baseline.json` — od tego momentu bramka slopwatch w `tools/Test-TopicReady.ps1` jest aktywna.
- Rollback: revert commitów tematu.

## Zgoda na zakres i odstępstwa

- **Akceptacja planu**: zaakceptowano — nog_programista2, 2026-11-04.
- **Odstępstwa**:
  1. **Kolejność kroków**: krok 3 (`Worker.Tests/TestWaits.cs`) wykonany przed krokiem 1, bo testy z kroku 1 korzystają z pomocnika. Czerwony przebieg z kroku 1 zachowany (2/55 — brak logu `Debug`), potem krok 2.
  2. **Filtr logów w `WorkerTests.BuildHost`** (w zakresie kroku 4): `AddFilter("DataAcquisition", LogLevel.Debug)` zamiast `SetMinimumLevel(LogLevel.Debug)` — reguła `Default` z `appsettings.json` workera (kopiowanego do katalogu testów) ma pierwszeństwo przed `SetMinimumLevel`, więc log `Debug` nie był zbierany; filtr kategorii jest bardziej szczegółowy i wygrywa.
  3. Nazwy testów `RunAsync_Stopped…` rozszerzone o `…AndLogsStop` (nowa asercja AC-03).
