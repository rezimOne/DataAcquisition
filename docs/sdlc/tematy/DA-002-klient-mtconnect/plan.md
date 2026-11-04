# Plan: DA-002 — Klient MTConnect w workerze

Status: approved. Tryb: feature. Baza: 417e369. Odpowiedzialny: nog_programista2. Źródło: [spec](spec.md).

> Cykl statusu: draft → approved → done. Status approved nadaje wyłącznie człowiek (hook blokuje agenta). Tylko przy statusie approved hook pozwala edytować pliki wymienione w tabeli poniżej — ścieżki względem korzenia repozytorium, w backtickach; dozwolony katalog zakończony ukośnikiem lub wzorzec z gwiazdką (nie może zaczynać się od gwiazdki).
> Tryb fix — testy zacommitowane w HEAD są zablokowane do edycji: najpierw commit z czerwonym testem, potem poprawka bez zmiany testu.

## Pliki i kolejność zmian

Uwaga: plan celowo nie zawiera bloków kodu z potrójnymi backtickami — psują rozpoznawanie ścieżek w `Test-PlanCovers` (zob. DA-001, odstępstwo 6).

| Krok | Plik / Komponent                       | Rodzaj zmiany | Opis                                                                                                                                                                                                                                                                                                                                                                                                                    | Zależność |
| ---- | -------------------------------------- | ------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| 1    | `Worker.Tests/Fixtures/`               | [NEW]         | Odpowiedzi XML zebrane z symulatora (`--seed 42`): `probe.xml`, `current.xml`, `sample.xml`, `sample-empty.xml`, `error-out-of-range.xml`; ręczne: `sample-conditions.xml` (`Fault` z `nativeCode`, `Normal` bez tekstu, `UNAVAILABLE`), `streams-foreign-namespace.xml`, `malformed.xml`                                                                                                                               | brak      |
| 2    | `Worker/MTConnect/`                    | [NEW]         | Model (`MTConnectObservation`, `ObservationBatch`, `StreamsHeader`, `DeviceModel`, enumy kategorii i poziomu), `MTConnectParser` (Devices/Streams/Error → model; sortowanie po `sequence`; rozpoznawanie namespace 1.3), `MTConnectResponse` (wynik: dokument / błąd agenta / błąd przejściowy)                                                                                                                         | Krok 1    |
| 3    | `Worker.Tests/MTConnect/`              | [NEW]         | Testy parsera na fixtures (AC-02) — pisane przed implementacją parsera (zaślepka → czerwone → zielone)                                                                                                                                                                                                                                                                                                                  | Krok 2    |
| 4    | `Worker/MTConnect/`                    | [NEW]         | `MTConnectOptions` + walidator (`DataAcquisition:MTConnect:*`, `ValidateOnStart`), `MTConnectClient` (typed `HttpClient`: `/probe`, `/current`, `/sample?from=&count=`, timeout, mapowanie statusów), `IObservationSink`, `LoggingObservationSink`, `MTConnectSynchronizer` (stany Niezsynchronizowany/Strumień, doganianie, resync, backoff przez `TimeProvider`), `MTConnectServiceCollectionExtensions`              | Krok 2    |
| 5    | `Worker/Worker.cs`                     | [MODIFY]      | `BackgroundService` uruchamia pętlę `MTConnectSynchronizer`; zatrzymanie przez `CancellationToken` bez wyjątku; usunięcie logu „Worker running at”                                                                                                                                                                                                                                                                      | Krok 4    |
| 6    | `Worker/Program.cs`                    | [MODIFY]      | Rejestracja `AddMTConnect(...)` (Options, typed client, sink, synchronizer) obok istniejących `TimeProvider.System` i hosted service                                                                                                                                                                                                                                                                                    | Krok 4    |
| 7    | `Worker/DataAcquisition.Worker.csproj` | [MODIFY]      | Pakiet `Microsoft.Extensions.Http` 10.0.12 (`AddHttpClient`); `InternalsVisibleTo` dla `DataAcquisition.Worker.Tests`                                                                                                                                                                                                                                                                                                   | Krok 4    |
| 8    | `Worker.Tests/`                        | [MODIFY]      | `WorkerTests.cs` — zastąpienie 3 testów szkieletu testami hosta (start/stop, walidacja Options — AC-07); `MTConnect/MTConnectSynchronizerTests.cs` — fałszywy agent + `FakeTimeProvider` + odbiorca rejestrujący (AC-03 – AC-06); `MTConnect/MTConnectClientTests.cs` — fałszywy `HttpMessageHandler` (URL, timeout, statusy); `DataAcquisition.Worker.Tests.csproj` — kopiowanie `Fixtures/**` do katalogu wyjściowego | Kroki 4–7 |

Test-first w drzewie roboczym (zaślepki `NotImplementedException` → testy czerwone → implementacja); commity per krok lub grupę kroków na gałęzi `topic/DA-002-klient-mtconnect`.

## Ryzyka

| Ryzyko                                                                                        | Wykrycie / ograniczenie                                                                                                                                                      |
| --------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Pętla gubi lub dubluje obserwacje przy przejściach stanów (resync, doganianie, błąd odbiorcy) | Testy synchronizatora ze ścisłą listą przekazanych sekwencji i wywołanych URL-i (AC-03 – AC-06); test ręczny ciągłości (AC-08)                                               |
| Testy zależne od czasu niestabilne (wyścig z `BackgroundService`)                             | Synchronizator testowany bezpośrednio (metoda jednego cyklu + pętla na `FakeTimeProvider`), bez realnych opóźnień; wzorzec oczekiwania na timer jak w obecnych `WorkerTests` |
| Wyjątek w pętli zatrzymuje host                                                               | Pętla łapie wszystko poza anulowaniem przy zatrzymaniu; test: wyjątek odbiorcy/klienta → pętla żyje (AC-06)                                                                  |
| Fixtures nie odpowiadają realnemu formatowi                                                   | Fixtures zebrane z działającego symulatora (krok 1), nie pisane ręcznie — poza świadomie ręcznymi przypadkami brzegowymi                                                     |
| Różnica kultur przy parsowaniu liczb/dat                                                      | Parsowanie `CultureInfo.InvariantCulture`; wartości obserwacji zostają tekstem (bez konwersji liczbowej)                                                                     |
| `Worker/appsettings*.json` niedostępne do edycji dla agenta (reguła odmowy odczytu)           | Domyślne wartości w klasie Options — pliki nie wymagają zmian                                                                                                                |
| Regresja API (DA-001)                                                                         | API i `Contracts/` nie są zmieniane; `dotnet test` całej solucji obejmuje `Api.Tests`                                                                                        |

## Weryfikacja

Komendy (kod 0 wymagany, output do `verification.md`):

- `dotnet build DataAcquisition.slnx`
- `dotnet test DataAcquisition.slnx`
- `dotnet format DataAcquisition.slnx --verify-no-changes`

| AC            | Dowód                                                                                                                                                                                                                                                  |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| AC-01         | Output trzech komend powyżej                                                                                                                                                                                                                           |
| AC-02         | Testy parsera na fixtures (nazwy testów ↔ AC w `verification.md`)                                                                                                                                                                                      |
| AC-03 – AC-06 | Testy `MTConnectSynchronizerTests` (lista URL-i, przekazane sekwencje, opóźnienia z `FakeTimeProvider`, logi z `FakeLogger`)                                                                                                                           |
| AC-07         | Testy startu hosta z błędną konfiguracją i zatrzymania w trakcie oczekiwania/żądania                                                                                                                                                                   |
| AC-08         | Test ręczny: `py resources/cnc_mtconnect_sim.py --seed 42` + `dotnet run --project Worker/DataAcquisition.Worker.csproj` ≥ 2 min, restart symulatora w trakcie; fragment logów w `verification.md` (ciągłość zakresów `sequence`, zmiana `instanceId`) |

Wymagania: .NET SDK 10, Python 3.9+ (symulator — tylko kroki 1 i AC-08). Bez sekretów.

## Wydanie i odtworzenie (Rollback)

- Brak migracji i trwałych danych.
- Nowa konfiguracja opcjonalna `DataAcquisition:MTConnect:*` (domyślnie agent `http://localhost:5000`); na serwerze zmienna np. `DataAcquisition__MTConnect__AgentUrl`.
- Worker zaczyna łączyć się z agentem — przy niedostępnym agencie loguje ostrzeżenia co ≤ 30 s (nie przerywa pracy).
- Rollback: revert commitów tematu (worker wraca do szkieletu) — API niezależne.

## Zgoda na zakres i odstępstwa

- **Akceptacja planu**: zaakceptowano — nog_programista2, 2026-11-04.
- **Odstępstwa**:
  1. **Named client zamiast typed client** (krok 4, spec *Dostęp i interfejsy*): `MTConnectClient` tworzy klienta przez `IHttpClientFactory.CreateClient("MTConnectAgent")` przy każdym żądaniu. Typed client wstrzyknięty do singletonu `MTConnectSynchronizer` trzymałby jeden `HttpClient`/handler przez cały czas życia procesu (brak rotacji handlerów, DNS). Konfiguracja (`BaseAddress` z końcowym `/`, `Timeout`) bez zmian. Dodatkowo `RemoveAllLoggers()` dla tego klienta — logi każdego żądania co `PollInterval` zagłuszałyby logi synchronizatora.
  2. **`sample-empty.xml` napisany ręcznie** (krok 1): symulator produkuje ~100 obserwacji/s, więc w 20 próbach `/sample?from=<nextSequence>` nie zwrócił pustej odpowiedzi; plik odwzorowuje `streams_xml` symulatora bez obserwacji (komentarz w pliku).
  3. **Test-first tylko dla parsera i synchronizatora** (czerwone 15/18 i 18/36 na zaślepkach); testy klienta HTTP i hosta (`MTConnectClientTests`, `WorkerTests`) napisane po implementacji klienta i rejestracji.
  4. **Tempo danych symulatora**: zmierzone ~100 obserwacji/s (fixtures: ~388 sekwencji w ~3,7 s), więc bufor 131072 starcza na ~22 min, a nie ~17 min jak szacował spec (*Ryzyka*) — wniosek bez zmian.
  5. **Doprecyzowanie spec (*Stany i cykl*, pkt 3.3) po teście ręcznym AC-08**: zrestartowany agent zwykle odpowiada na `/sample?from=<stary nextSequence>` błędem `OUT_OF_RANGE`, a nie dokumentem z nowym `instanceId`. Dlatego wykrywanie restartu (log `Information` + ponowne `/probe`) odbywa się w synchronizacji po `/current`, niezależnie od ścieżki; „`OUT_OF_RANGE` → bez `/probe`” obowiązuje tylko, gdy `instanceId` się nie zmienił. Test reprodukujący `3cb00ba`, poprawka `96142b5` (w zakresie `Worker/MTConnect/`).
  6. **Po review R-01 (decyzja człowieka)**: po resynchronizacji (`OUT_OF_RANGE`, restart agenta) `/sample` wznawia się od danych wciąż obecnych w buforze agenta (`firstSequence`, przy pełnym buforze z zapasem `SampleCount`), a nie od `nextSequence` z `/current`; `Warning` podaje faktycznie utracony zakres. Wymagało dodania `BufferSize` do `StreamsHeader` (parser czyta `Header/@bufferSize`). Zmienione oczekiwania w 3 testach synchronizatora (w tym w teście z `3cb00ba` — tylko oczekiwane `from`, bo zmieniło się zachowanie, tryb `feature`) + nowy test bufora niepełnego; asercje zakresu na wartościach strukturalnych logu (R-03). `spec.md` pkt 2.3 i 3.3 zaktualizowane.
  7. **Test ręczny poprawki R-01 (`--buffer 1000`, `PollInterval` 30 s) wykazał 2 błędy implementacji** (commit `bf41614`): po resynchronizacji worker czekał `PollInterval` przed `/sample` (dane z bufora wypadały, 0 paczek `/sample`), a zapas `SampleCount` (1000) równał się całemu buforowi (skok do `nextSequence`). Poprawka: `/sample` od razu po synchronizacji, zapas `min(SampleCount, bufferSize / 10)`. Testy: 3 czerwone (51/54) przed poprawką, 54/54 po; `spec.md` pkt 2.3–2.4.
