# Plan: DA-001 — API odbioru danych z maszyn

Status: approved. Tryb: feature. Baza: 495a418. Odpowiedzialny: nog_programista2. Źródło: [spec](spec.md).

> Cykl statusu: draft → approved → done. Status approved nadaje wyłącznie człowiek (hook blokuje agenta). Tylko przy statusie approved hook pozwala edytować pliki wymienione w tabeli poniżej — ścieżki względem korzenia repozytorium, w backtickach; dozwolony katalog zakończony ukośnikiem lub wzorzec z gwiazdką (nie może zaczynać się od gwiazdki).
> Tryb fix — testy zacommitowane w HEAD są zablokowane do edycji: najpierw commit z czerwonym testem, potem poprawka bez zmiany testu.

## Pliki i kolejność zmian

Układ katalogów zgodny z istniejącym (`Worker/`, `Worker.Tests/` w korzeniu repozytorium). Nowe projekty: `net10.0`, `Nullable` i `ImplicitUsings` włączone jak w `Worker`.

| Krok | Plik / Komponent       | Rodzaj zmiany | Opis                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | Zależność |
| ---- | ---------------------- | ------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| 1    | `Contracts/`           | [NEW]         | Projekt `DataAcquisition.Contracts` (`Microsoft.NET.Sdk`, bez zależności od ASP.NET): `ObservationBatchRequest`, `ObservationDto`, `ObservationCategory`, `ConditionLevel`, `ObservationBatchResponse`, `ObservationQueryResponse`, `StoredObservationDto` — rekordy `sealed`, enumy serializowane jako stringi                                                                                                                                                                                                                                                                                                                                                                                                                                                             | brak      |
| 2    | `Api/`                 | [NEW]         | Projekt `DataAcquisition.Api` (`Microsoft.NET.Sdk.Web`, Minimal API, własny `UserSecretsId`): `Program.cs` (DI, `AddProblemDetails`, `AddHealthChecks`, mapowanie endpointów), `ApiKey/ApiKeyOptions.cs` + walidacja `ValidateOnStart` (min. 32 znaki), `ApiKey/ApiKeyEndpointFilter.cs` (`X-Api-Key`, `FixedTimeEquals`, 401), `Observations/ObservationEndpoints.cs` (grupa `/api/v1`, POST/GET), `Observations/ObservationBatchValidator.cs` (ręczne reguły ze spec, ścieżki pól), `Observations/IObservationStore.cs`, `Observations/InMemoryObservationStore.cs` (singleton, bezpieczny wątkowo, klucz `deviceId`+`instanceId`+`sequence`), `appsettings.json`/`appsettings.Development.json` (bez klucza), `Properties/launchSettings.json` (`http://localhost:5100`) | Krok 1    |
| 3    | `Api.Tests/`           | [NEW]         | Projekt `DataAcquisition.Api.Tests` (xUnit v3 jak `Worker.Tests` + `Microsoft.AspNetCore.Mvc.Testing` 10.0.x, `Microsoft.Extensions.Diagnostics.Testing`): `ApiFactory` (fikcyjny klucz testowy, przechwycenie logów `FakeLogger`), testy integracyjne AC-02–AC-08, testy jednostkowe `ObservationBatchValidator` i `InMemoryObservationStore` (w tym równoległy zapis)                                                                                                                                                                                                                                                                                                                                                                                                     | Krok 2    |
| 4    | `DataAcquisition.slnx` | [MODIFY]      | Dodanie 3 projektów do solucji (`dotnet sln add`)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | Kroki 1–3 |

Kod pisany test-first w obrębie kroków 2–3 (testy walidatora i store przed implementacją), commity per krok na gałęzi `topic/DA-001-api-odbioru-danych`.

## Ryzyka

| Ryzyko                                                                             | Wykrycie / ograniczenie                                                                                   |
| ---------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Wyścig przy równoległym zapisie tych samych kluczy (podwójne `accepted`)           | Store oparty o `ConcurrentDictionary.TryAdd` per klucz; test z równoległymi paczkami (AC-05)              |
| Wyciek klucza API do logów (np. logowanie nagłówków przez `HttpLogging`)           | Nie włączamy logowania nagłówków; test AC-08 przeszukuje przechwycone logi pod kątem wartości klucza      |
| Sekret w repozytorium                                                              | Klucz tylko w User Secrets / zmiennej środowiskowej; klucz w testach jawnie fikcyjny; skan sekretów hooka |
| Błędy deserializacji (zły enum, zły format daty) zwracają 400 bez `ProblemDetails` | `AddProblemDetails` + test AC-04 dla niepoprawnego JSON sprawdza `application/problem+json`               |
| Testy integracyjne niestabilne (port, równoległość)                                | `WebApplicationFactory` z `TestServer` (bez realnego portu); osobna instancja fabryki na klasę testów     |
| Regresja workera                                                                   | Worker nie jest zmieniany; `dotnet test` całej solucji obejmuje `Worker.Tests`                            |
| Rozrost pamięci store'a                                                            | Świadomie zaakceptowane (spec, Ryzyka); trwała baza w osobnym temacie                                     |

## Weryfikacja

Komendy (kod 0 wymagany, output wklejany do `verification.md`):

- `dotnet build DataAcquisition.slnx`
- `dotnet test DataAcquisition.slnx`
- `dotnet format DataAcquisition.slnx --verify-no-changes`

| AC            | Dowód                                                                                                         |
| ------------- | ------------------------------------------------------------------------------------------------------------- |
| AC-01         | Output `dotnet build` i `dotnet test` (3 projekty testowe/produkcyjne w solucji, testy workera zielone)       |
| AC-02 – AC-07 | Nazwane testy integracyjne w `Api.Tests` (nazwa testu ↔ AC w `verification.md`)                               |
| AC-08         | Test startu bez klucza / z krótkim kluczem; test logów; `grep` po `Api/appsettings*.json` bez wartości klucza |

Test ręczny (opcjonalny, wynik w `verification.md`): `dotnet user-secrets set "DataAcquisition:Api:ApiKey" <klucz> --project Api`, `dotnet run --project Api`, `curl` POST/GET/health na `http://localhost:5100`.

Wymagania: .NET SDK 10. Bez bazy danych, bez zewnętrznych usług, bez prawdziwych sekretów.

## Wydanie i odtworzenie (Rollback)

- Brak migracji bazy (store w pamięci).
- Nowa wymagana konfiguracja: `DataAcquisition__Api__ApiKey` (zmienna środowiskowa, min. 32 znaki) — bez niej API nie startuje.
- Nowy proces/usługa `DataAcquisition.Api`; worker bez zmian, więc rollback = niewdrożenie / zatrzymanie API albo revert commitów tematu (brak danych trwałych do odtworzenia).
- Sugestia dla człowieka (poza zakresem agenta): ustawić `testCommand` w `.claude/sdlc.json` na `["dotnet", "test", "DataAcquisition.slnx", "--nologo"]`, żeby `tools/Test-TopicReady.ps1` uruchamiał testy przed pushem.

## Zgoda na zakres i odstępstwa

- **Akceptacja planu**: zaakceptowano — nog_programista2, 2026-11-04
- **Odstępstwa**:
  1. ~~`Api/appsettings.json` i `Api/appsettings.Development.json` nie zostały utworzone~~ — **rozwiązane**: zapis był zablokowany regułą uprawnień agenta (deny dla `appsettings*`), pliki utworzył człowiek (`Microsoft.AspNetCore = Warning`, `AllowedHosts = *`; Development: `DataAcquisition = Debug`). Agent nie może odczytać ich treści — działanie potwierdzone zachowaniem uruchomionego API (`verification.md`).
  2. Wymóg spec „`timestamp` z przesunięciem” nie jest egzekwowany: `System.Text.Json` przyjmuje datę bez strefy jako czas lokalny serwera. Worker MTConnect wysyła czasy z `Z`, więc ryzyko jest niskie; wymuszenie (własny konwerter w `Contracts/`) — do decyzji w kolejnym temacie.
  3. Test-first realizowany w drzewie roboczym (zaślepki `NotImplementedException` → testy czerwone 61/73 → implementacja → 73/73), a commity per krok planu, więc commit `Api/` poprzedza commit `Api.Tests/`.
  4. Klucz API w testach generowany losowo w czasie wykonania zamiast stałej — hook skanu sekretów odrzucił literał.
  5. Nazwa klucza konfiguracji zmieniona z `ApiKey:Value` na `DataAcquisition:Api:ApiKey` (zmienna `DataAcquisition__Api__ApiKey`) na prośbę człowieka — nazwa ma jednoznacznie wskazywać projekt w User Secrets i zmiennych środowiskowych. Zmiana w `Api/ApiKey/`, `Api.Tests/` i `spec.md` (spec zatwierdzony — zmiana kontraktu konfiguracji za zgodą człowieka, 2026-11-04).
  6. Gałąź zawiera zmianę `.claude/sdlc.json` (commit `183930d`, `testCommand` dla bramki `tools/Test-TopicReady.ps1`) — wprowadzoną przez człowieka zgodnie z sugestią z sekcji *Wydanie*; to konfiguracja procesu, nie kod tematu. Przy okazji blok kodu z komendami w sekcji *Weryfikacja* zamieniony na listę (treść bez zmian): potrójne backticki rozjeżdżały parowanie ścieżek w `Test-PlanCovers`, przez co plik nie był rozpoznawany jako objęty planem — błąd narzędzia do zgłoszenia człowiekowi (`tools/` i `.claude/hooks/` zmienia tylko człowiek).
  7. Uwierzytelnianie zrealizowane jako schemat ASP.NET Core (`Api/ApiKey/ApiKeyAuthenticationHandler.cs` + `Api/ApiKey/ApiKeyServiceCollectionExtensions.cs`, grupa endpointów z `RequireAuthorization`) zamiast planowanego `ApiKeyEndpointFilter.cs`. Powód: endpoint filter działa **po** bindowaniu body, więc żądanie bez klucza z niepoprawnym JSON dostałoby `400` zamiast `401`, a treść byłaby czytana przed uwierzytelnieniem (wbrew spec, *Zapis* pkt 6). Pokryte testem `Post_WithoutApiKeyAndInvalidBody_Returns401NotValidationError`. (Uwaga review R-04.)
  8. Po review (R-01) dodany `Contracts/StrictStringEnumConverter.cs` — enumy w JSON wyłącznie jako stringi (`allowIntegerValues: false`), plus `Enum.IsDefined` w walidatorze. Po review (R-02) `spec.md` zmieniony: zły `Content-Type` → `415` zamiast `400` (decyzja człowieka).
