# Review: DA-001 — API odbioru danych z maszyn

Status: changes-requested. Commit: 8344188. Baza: 495a418. Recenzent: sdlc-reviewer. Data: 2026-11-04.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review DA-001` lub `tools/Invoke-Review.ps1 -Id DA-001`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|
| R-01 | P2 | `Api/Observations/ObservationBatchValidator.cs:61`, `:78`; `Contracts/ObservationCategory.cs:7`; `Contracts/ConditionLevel.cs:6` | `JsonStringEnumConverter<T>()` domyślnie ma `AllowIntegerValues = true` i nie sprawdza `Enum.IsDefined`. Worker (lub inny klient z kluczem) wysyła `{"category": 7, ...}` albo `{"category":"Condition","conditionLevel": 42}`. Deserializacja się udaje, walidator sprawdza tylko `Category is null`, a reguła `conditionLevel` (linie 78–86) nie obejmuje kategorii spoza `Sample`/`Event`/`Condition`, więc obserwacja z `category = 7` przechodzi bez żadnego sprawdzenia `conditionLevel`. Wynik: `200`, rekord zapisany, a GET zwraca `"category":7` / `"conditionLevel":42` jako liczby — wbrew spec („enum `Sample`/`Event`/`Condition` — jedna z wartości”, „enumy jako stringi”) i intent („odrzuca dane niepoprawne”). Test `Post_MalformedJson_…("Bogus")` pokrywa tylko niepoprawny string, nie liczbę. Poprawka: `Enum.IsDefined` dla `Category` i `ConditionLevel` w walidatorze (błąd pod `observations[i].category` / `.conditionLevel`) i/lub `allowIntegerValues: false` w konwerterach w `Contracts/`; dodać test integracyjny i jednostkowy dla wartości liczbowej. | otwarta |
| R-02 | P3 | `Api/Observations/ObservationEndpoints.cs:16` | Spec, *Zapis* pkt 7: „Niepoprawny JSON / zły `Content-Type` → `400` (`ProblemDetails`)”. Minimal API (ASP.NET Core 7+) przy parametrze body wnioskowanym z JSON zwraca dla `Content-Type: text/plain` lub braku nagłówka `415 Unsupported Media Type`, nie `400`. Scenariusz: worker w kolejnym temacie wysyła treść bez `application/json` i — zgodnie ze spec — obsługuje `400` jako „paczka do poprawy”, a dostaje `415`, którego nie przewidział. Brak testu dla złego `Content-Type`, więc rozbieżność nie jest widoczna w `verification.md`. Proponowane: dodać test i albo zaktualizować spec na `415` (zachowanie bardziej poprawne semantycznie), albo świadomie mapować na `400`. | otwarta |
| R-03 | P3 | `docs/sdlc/tematy/DA-001-api-odbioru-danych/release.md:12` | `release.md` jest niewypełnionym szablonem (`Commit: 495a418`, placeholdery), mimo że zmiana wprowadza nową wymaganą konfigurację `DataAcquisition__Api__ApiKey` (min. 32 znaki), bez której proces nie startuje. REVIEW.md, filar 5, wymaga instrukcji konfiguracji nowych zmiennych w `release.md`. Scenariusz: osoba uruchamiająca API na innym środowisku czyta `release.md`, nie znajduje zmiennej, start kończy się `OptionsValidationException`. Skutek łagodzi fail-fast z czytelnym komunikatem i opis w `plan.md` (*Wydanie*), stąd P3. Wdrożenie jest poza zakresem tematu — wystarczy sekcja konfiguracji i rollbacku przeniesiona z planu. | otwarta |
| R-04 | P3 | `docs/sdlc/tematy/DA-001-api-odbioru-danych/plan.md:15` | Plan (krok 2) przewiduje `ApiKey/ApiKeyEndpointFilter.cs` (endpoint filter), a implementacja to `ApiKey/ApiKeyAuthenticationHandler.cs` + `ApiKey/ApiKeyServiceCollectionExtensions.cs` (schemat uwierzytelniania + `RequireAuthorization`). Zmiana jest uzasadniona (401 przed bindowaniem body — test `Post_WithoutApiKeyAndInvalidBody_Returns401NotValidationError`) i opisana w `pr.md`, ale nie w sekcji *Odstępstwa* planu, czego wymaga CLAUDE.md pkt 2. Hook pozwolił na edycję, bo plan dopuszcza cały katalog `Api/`, więc rozbieżność nie zostanie wychwycona automatycznie. Scenariusz: w kolejnym temacie (klient API w workerze) ktoś opiera się na planie DA-001 i szuka filtra, którego nie ma. Proponowane: dopisać odstępstwo 7. | otwarta |

## Zgodność z planem

- Wszystkie pliki spoza `docs/` mieszczą się w zakresie planu: `Contracts/`, `Api/`, `Api.Tests/` (katalogi dozwolone), `DataAcquisition.slnx` (krok 4). Tabela *Zmienione pliki* w `pr.md` nie zawiera pozycji **NIE**.
- `.claude/sdlc.json` (commit `183930d`) — zmiana człowieka (`testCommand` zgodny z sugestią z sekcji *Wydanie*), opisana jako odstępstwo 6. Oprócz `testCommand` plik został przeformatowany (rozbicie tablic `_przyklady`, brak końcowego znaku nowej linii) — bez wpływu na działanie.
- Odstępstwa 1–6 są opisane. Odstępstwo 2 (`timestamp` bez przesunięcia przyjmowany jako czas lokalny serwera) jest realnym odejściem od zatwierdzonej spec — przy kliencie wysyłającym `2026-11-04T10:15:30` bez `Z` zapis zostanie przesunięty o strefę serwera. Zostało jawnie przekazane do decyzji człowieka w `pr.md`; nie dublujemy go jako uwagi, ale wymaga świadomej akceptacji przy decyzji w `pr.md`.
- Zmiana mechanizmu uwierzytelniania (endpoint filter → `AuthenticationHandler`) nie jest w *Odstępstwach* — R-04.
- Tryb: `feature` — kontrola niezmienności testu reprodukującego nie dotyczy.

## Pokrycie kryteriów akceptacji

| AC | Test / dowód w verification.md | Ocena recenzenta |
|---|---|---|
| AC-01 | `dotnet build` / `dotnet test` / `dotnet format` z kodem 0 dla drzewa `d728000`; `DataAcquisition.slnx` zawiera 3 nowe projekty (diff) | pokryte |
| AC-02 | `Post_ValidBatchWithSampleAndCondition_AcceptsAndReturnsFromQuery` (porównanie rekordów 1:1, konwersja +02:00 → UTC), `Post_ValidBatch_UsesCamelCaseAndStringEnumsOnWire`; test ręczny curl | pokryte |
| AC-03 | `Post_WithoutApiKey_…`, `Post_WithInvalidApiKey_…` (oba sprawdzają brak zapisu), `Post_WithoutApiKeyAndInvalidBody_…`, `Get_WithoutApiKey_Returns401` | pokryte |
| AC-04 | `Post_InvalidBatch_…` (8 przypadków ze spec), `Post_MoreThanMaxBatchSize_…`, `Post_InvalidObservationField_ErrorKeyPointsToField`, `Post_MalformedJson_…` + testy walidatora | pokryte dla przypadków ze spec; luka dla enumów podanych liczbą (R-01) i dla złego `Content-Type` (R-02) |
| AC-05 | `Post_SameBatchTwice_…`, `Post_OverlappingBatch_…`, `Post_ParallelIdenticalBatches_…` (4 × 200), `AddAsync_ConcurrentIdenticalBatches_…` (8 × 500) | pokryte |
| AC-06 | `Get_FilterByDeviceTimeRangeAndLimit_…` (2 urządzenia, 2 instancje, `[from, to)`, `limit=2`, `truncated`), `Get_InvalidParameters_…` (5 przypadków), `Get_UnknownDevice_…`, `QueryAsync_*` | pokryte |
| AC-07 | `GetHealth_WithoutApiKey_Returns200Healthy`; test ręczny | pokryte |
| AC-08 | `Startup_MissingOrTooShortApiKey_…` (null, pusty, 31 znaków; komunikat bez wartości), `Startup_ApiKeyOfMinimumLength_Starts`, `Requests_WithValidAndInvalidKeys_NeverLogKeyValues`, `AddAsync_DuplicateWithDifferentContent_LogsWarningWithoutValue`; test ręczny `grep` = 0 | pokryte; zastrzeżenie z `verification.md` zamknięte przez recenzenta — patrz niżej |

## Dowody weryfikacji

- `verification.md` ma rzeczywiste outputy (`BUILD_EXIT=0`, `TEST_EXIT=0`, `FORMAT_EXIT=0`, Api.Tests 73/73, Worker.Tests 3/3) dla drzewa `d728000`. Commity po `d728000` (`507e7be`, `19ed643`, `ef5d564`, `8344188`) zmieniają wyłącznie `docs/` — dowody pozostają aktualne dla przejrzanego kodu w `8344188`.
- Lista testów odpowiada testom w diffie (73 przypadki Api.Tests); przebieg czerwony 61/73 na zaślepkach dokumentuje test-first (odstępstwo 3).
- **Zastrzeżenie AC-08 (`Api/appsettings*.json`)**: recenzent przeczytał treść obu plików w diffie — zawierają wyłącznie `Logging:LogLevel` (`Default`, `Microsoft.AspNetCore`, w Development także `DataAcquisition: Debug`) i `AllowedHosts`. Brak klucza `DataAcquisition:Api:ApiKey` ani innych sekretów. Poziom `Debug` dla `DataAcquisition.*` powoduje logi `AuthenticationHandler` („was not authenticated” / „Failure message: Invalid API key.”), które nie zawierają wartości nagłówka.
- W diffie nie ma literałów kluczy: `ApiFactory.TestApiKey` jest losowany w czasie wykonania, w testach są tylko jawnie fikcyjne wartości (`wrong-key…`, 31-znakowy krótki klucz, `new string('k', 32)`).
- Pominięcia w `verification.md` (brak testu `timestamp` bez strefy, brak testów obciążeniowych, brak integracji z workerem, HTTP bez TLS) są wyjaśnione i zgodne z zakresem. Brakuje testu złego `Content-Type`, który spec wymienia wprost (R-02).

## Historia iteracji

| Iteracja | Commit | Data | Wynik |
|---|---|---|---|
| 1 | 8344188 | 2026-11-04 | changes-requested: P1 0, P2 1, P3 3 |
