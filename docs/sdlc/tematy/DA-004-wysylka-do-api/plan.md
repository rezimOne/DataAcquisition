# Plan: DA-004 — Wysylka obserwacji z workera do API

Status: draft. Tryb: feature. Baza: 751df55. Odpowiedzialny: nog_programista2. Źródło: [spec](spec.md).

> Cykl statusu: draft → approved → done (done ustawia człowiek przy akceptacji PR, przed pushem). Status approved nadaje wyłącznie człowiek (hook blokuje agenta). Tylko przy statusie approved hook pozwala edytować pliki wymienione w tabeli poniżej — ścieżki względem korzenia repozytorium, w backtickach; dozwolony katalog zakończony ukośnikiem lub wzorzec z gwiazdką (nie może zaczynać się od gwiazdki).
> Tryb fix — testy zacommitowane w HEAD są zablokowane do edycji: najpierw commit z czerwonym testem, potem poprawka bez zmiany testu.

## Pliki i kolejność zmian

Plan bez bloków kodu z potrójnymi backtickami (`Test-PlanCovers`). Każdy krok kodu: najpierw testy (czerwone na zaślepce / starym kodzie), potem implementacja; commit per grupa kroków.

| Krok | Plik / Komponent | Rodzaj zmiany | Opis | Zależność |
|---|---|---|---|---|
| 1 | `Contracts/ObservationBatchRequest.cs`, `Contracts/ObservationResponses.cs` | [MODIFY] | `ObservationDto` i `StoredObservationDto`: opcjonalne `Units`, `NativeCode` (domyślnie `null`, na końcu listy parametrów — kompatybilność wsteczna) | brak |
| 2 | `Api/Observations/ObservationBatchValidator.cs`, `Api/Observations/ObservationEndpoints.cs` | [MODIFY] | Walidacja długości `units`/`nativeCode` (≤ 64); przeniesienie pól do zapisu i odczytu | Krok 1 |
| 3 | `Api.Tests/` | [MODIFY] | Testy: nowe pola zapisane i zwrócone 1:1; paczka bez nowych pól nadal poprawna; za długie `units`/`nativeCode` → 400 z kluczem pola | Krok 2 |
| 4 | `Worker/DataAcquisition.Worker.csproj` | [MODIFY] | `ProjectReference` do `Contracts/DataAcquisition.Contracts.csproj` | Krok 1 |
| 5 | `Worker/Api/` | [NEW] | `ApiOptions` + walidator (`DataAcquisition:Api:*`, `ValidateOnStart`), `ObservationBatchMapping` (jawne mapowanie na kontrakt), `ApiObservationSink` (named `HttpClient`, `X-Api-Key`, klasyfikacja odpowiedzi wg spec, logi bez wartości i klucza), `ApiServiceCollectionExtensions` (`AddDataAcquisitionApi()`: Options, klient z `RemoveAllLoggers`, rejestracja odbiorcy jako `IObservationSink`) | Krok 4 |
| 6 | `Worker/MTConnect/ObservationSinks.cs`, `Worker/MTConnect/MTConnectServiceCollectionExtensions.cs` | [MODIFY] | Usunięcie `LoggingObservationSink` i jego domyślnej rejestracji (zostaje interfejs `IObservationSink`) | Krok 5 |
| 7 | `Worker/Program.cs` | [MODIFY] | `AddDataAcquisitionApi()` obok `AddMTConnect()` | Kroki 5–6 |
| 8 | `Worker.Tests/` | [MODIFY] | `Api/ApiObservationSinkTests.cs` (fałszywy handler: URL, nagłówek, treść JSON z wszystkimi polami; 200/400/401/403/5xx/404/timeout/brak połączenia; logi bez klucza i wartości), `Api/ObservationBatchMappingTests.cs`, `Api/ApiOptionsTests` lub rozszerzenie `WorkerTests.cs` (walidacja konfiguracji, domyślna rejestracja odbiorcy, `BuildHost` z kluczem testowym) | Kroki 5–7 |
| 9 | `Integration.Tests/` | [NEW] | Nowy projekt testów (xUnit v3, `Microsoft.AspNetCore.Mvc.Testing`) referencjonujący `Api` i `Worker`: odbiorca → API w `WebApplicationFactory` → `GET` (AC-02); przerwa API (handler odrzucający N żądań) + synchronizator z fałszywym agentem → komplet bez duplikatów (AC-03). Fabryka typowana klasą z `Api` (nie `Program` — oba projekty mają top-level `Program`) | Kroki 2, 5 |
| 10 | `DataAcquisition.slnx` | [MODIFY] | Dodanie `Integration.Tests` | Krok 9 |

## Wymagane skille

- `dotnet-skills:csharp-coding-standards`, `dotnet-skills:csharp-nullable-reference-types` — wszystkie kroki z kodem C#.
- `dotnet-skills:csharp-api-design` — kroki 1–3 (zmiana publicznego kontraktu w `Contracts/`, kompatybilność wsteczna).
- `dotnet-skills:serialization` — kroki 1, 5 (DTO, JSON, enumy jako stringi).
- `dotnet-skills:microsoft-extensions-dependency-injection`, `dotnet-skills:microsoft-extensions-configuration` — kroki 5–7 (rejestracja, Options z walidacją).
- `dotnet-skills:csharp-concurrency-patterns` — krok 5 (async, anulowanie, `HttpClient`).
- `dotnet-skills:project-structure`, `dotnet-skills:package-management` — kroki 4, 9, 10 (`*.csproj`, `.slnx`, pakiety).
- `dotnet-skills:slopwatch` — cała zmiana (AC-01).

## Ryzyka

| Ryzyko | Wykrycie / ograniczenie |
|---|---|
| Zmiana kontraktu psuje istniejących klientów/testy | Pola opcjonalne na końcu rekordów; test paczki bez nowych pól; cały `Api.Tests` zielony |
| Klucz API w logach (nagłówek, wyjątek) | `RemoveAllLoggers` na kliencie; testy przeszukują przechwycone logi pod kątem klucza (worker i E2E) |
| Pominięcie paczki przy 400 ukrywa błąd mapowania | Testy mapowania 1:1 i E2E na prawdziwym API; log `Error` z nazwami pól |
| Konflikt nazw `Program` / plików `appsettings.json` przy referencji do dwóch projektów wykonywalnych w `Integration.Tests` | Fabryka typowana klasą z `Api`; ewentualny konflikt kopiowania `appsettings*.json` rozwiązany w kroku 9 i opisany jako odstępstwo |
| Worker nie startuje u dewelopera bez klucza w User Secrets projektu Worker | Instrukcja w `release.md` i `CLAUDE.md`-owe komendy bez zmian; komunikat walidacji wskazuje klucz |
| Niestabilne testy E2E | `TestServer` (bez portów), `FakeTimeProvider` dla backoffu, bez `Task.Delay` (slopwatch) |

## Weryfikacja

Komendy (kod 0, output do `verification.md`, kody wyjścia bez potoku):

- `dotnet build DataAcquisition.slnx`
- `dotnet test DataAcquisition.slnx --nologo`
- `dotnet format DataAcquisition.slnx --verify-no-changes`
- `dotnet tool run slopwatch analyze --fail-on warning`

| AC | Dowód |
|---|---|
| AC-01 | Output czterech komend |
| AC-02 | Testy `ApiObservationSinkTests` (treść żądania) + E2E w `Integration.Tests` |
| AC-03 | Testy odbiorcy (błędy przejściowe) + E2E z przerwą API |
| AC-04 | Testy 400/401/403 z asercjami logów (poziom, brak wartości i klucza) |
| AC-05 | Testy startu hosta workera z błędną konfiguracją; przeszukanie logów pod kątem klucza |
| AC-06 | Test ręczny: `py resources/cnc_mtconnect_sim.py --seed 42`, `dotnet run --project Api`, `dotnet run --project Worker/DataAcquisition.Worker.csproj` (klucz przez zmienną środowiskową, losowy), restart API w trakcie; porównanie `GET` z logami workera |

Wymagania: .NET SDK 10, Python 3.9+ (AC-06). Klucz w testach losowany w czasie wykonania (jak w DA-001).

## Wydanie i odtworzenie (Rollback)

- Brak migracji (API w pamięci).
- **Nowa wymagana konfiguracja workera**: `DataAcquisition__Api__ApiKey` (ten sam klucz co API), opcjonalnie `DataAcquisition__Api__BaseUrl`, `DataAcquisition__Api__RequestTimeout` — bez klucza worker nie startuje.
- Kontrakt API rozszerzony kompatybilnie — kolejność wdrożenia: najpierw API, potem worker.
- Rollback: revert commitów tematu (worker wraca do odbiorcy logującego).

## Zgoda na zakres i odstępstwa

- **Akceptacja planu**: <osoba, rola, data> — wpisuje człowiek. Nie dopisuj zgody bez faktycznego potwierdzenia.
- **Odstępstwa**: <zmiany planu w trakcie implementacji wraz z uzasadnieniem; aktualizuj w tym samym commicie co kod, którego dotyczą>.
