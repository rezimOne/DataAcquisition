# Weryfikacja: DA-004

Status: verified. Commit: df35353. Środowisko: Windows 11 Pro, .NET SDK 10.0.401, runtime 10.0.12, slopwatch 0.4.2 (baseline pusty, `--fail-on warning`), Python 3.14.7 (symulator `--seed 42`). Klucz API w testach automatycznych losowany w czasie wykonania; w teście ręcznym losowy (`daq-api-` + `secrets.token_hex(24)`), przekazany tylko zmiennymi środowiskowymi. Data: 2026-11-05.

## Wyniki testów vs Kryteria Odbioru

| AC | Test / Kroki weryfikacji | Oczekiwany wynik | Wynik rzeczywisty | Dowód (log / output / zrzut) |
|---|---|---|---|---|
| AC-01 | `dotnet build`, `dotnet test --nologo`, `dotnet format --verify-no-changes`, `dotnet tool run slopwatch analyze --fail-on warning` | Kod 0, slopwatch 0 znalezisk | PASS | [Build, testy, format, slopwatch](#build-testy-format-slopwatch) |
| AC-02 | `ApiObservationSinkTests.ReceiveAsync_Accepted_PostsContractJsonWithApiKeyAndLogsResult`, `…BaseUrlWithPath_KeepsPath`, `ObservationBatchMappingTests` (8), `WorkerToApiTests.Sink_SendsBatch_ApiReturnsSameObservationsWithUnitsAndNativeCode`, `Api.Tests` (`Post_WithUnitsAndNativeCode_…`, `Post_PayloadWithoutUnitsAndNativeCode_…`, walidacja długości); test ręczny | Żądanie zgodne z kontraktem i kluczem; dane w API 1:1 z `units`/`nativeCode`; stare paczki nadal przyjmowane | PASS | [Lista testów](#nowe-testy), [Test ręczny](#test-ręczny-ac-06) |
| AC-03 | `…TransientStatus_ThrowsSoBatchIsRetried` (500, 503, 404, 429), `…ConnectionFailure_Throws`, `…ApiDoesNotAnswer_TimesOutAfterRequestTimeout`, `WorkerToApiTests.Synchronizer_ApiUnavailableForTwoRequests_RetriesAndStoresEveryObservationOnce`; test ręczny z restartem API | Wyjątek → ponowienie tej samej paczki z backoffem (1 s, 2 s); po powrocie API komplet bez duplikatów | PASS | [Lista testów](#nowe-testy), [Test ręczny](#test-ręczny-ac-06) |
| AC-04 | `…ValidationProblem_LogsFieldNamesWithoutValuesAndSkipsBatch`, `…BadRequestWithoutProblemDetails_LogsAndSkipsBatch`, `…ApiKeyRejected_LogsErrorAndThrowsSoBatchIsRetried` (401, 403) | 400: `Error` z nazwami pól, bez wartości, bez wyjątku; 401/403: `Error` + wyjątek; brak klucza i wartości w logach | PASS | [Lista testów](#nowe-testy) |
| AC-05 | `WorkerTests.StartAsync_InvalidApiConfiguration_FailsWithConfigurationKeyWithoutValue` (6), `Options_ApiWithKeyOnly_UsesLocalApiDefaults`, `Services_DefaultRegistration_UsesApiSinkAndHttpAgent`; `grep` klucza w logach procesów (test ręczny) | `OptionsValidationException` z pełnym kluczem, bez wartości; klucz nieobecny w logach | PASS | [Lista testów](#nowe-testy), [Test ręczny](#test-ręczny-ac-06) |
| AC-06 | Symulator + API + worker 150 s; API zatrzymane po 60 s na 20 s i uruchomione ponownie; odczyt wszystkich obserwacji z API oknami 2 s; porównanie z logiem workera | Liczba obserwacji w API = suma `accepted` po restarcie API; brak luk i duplikatów | PASS | [Test ręczny](#test-ręczny-ac-06) |

## Dowody

### Build, testy, format, slopwatch

Drzewo = `df35353` (czyste):

```text
$ dotnet build DataAcquisition.slnx --nologo
    0 Warning(s)
    0 Error(s)
BUILD_EXIT=0
$ dotnet test DataAcquisition.slnx --nologo --no-build
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 640 ms - DataAcquisition.Integration.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    82, Skipped:     0, Total:    82, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    86, Skipped:     0, Total:    86, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
TEST_EXIT=0
$ dotnet format DataAcquisition.slnx --verify-no-changes
FORMAT_EXIT=0
$ dotnet tool run slopwatch analyze --fail-on warning
Scan complete: 0 issue(s) found
SLOPWATCH_EXIT=0
```

Przebiegi czerwone (test-first, drzewo robocze przed commitami implementacji):

```text
# kontrakt rozszerzony, API bez zmian (przed 9aed68d)
  Failed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_TooLongUnitsAndNativeCode_ReturnsIndexedFieldErrors
  Failed DataAcquisition.Api.Tests.ObservationsApiTests.Post_WithUnitsAndNativeCode_StoresAndReturnsThem
Failed!  - Failed:     2, Passed:    84, Skipped:     0, Total:    86, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
# mapowanie i odbiorca jako zaślepki NotImplementedException (przed 3818320)
Failed!  - Failed:    20, Passed:    62, Skipped:     0, Total:    82, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
     10 System.NotImplementedException : The method or operation is not implemented.
      7 Assert.Throws() Failure: Exception type was not an exact match
      2 Assert.Null() Failure: Value is not null
      1 Assert.IsAssignableFrom() Failure: Value is an incompatible type
```

Testy end-to-end (`Integration.Tests`) napisane po implementacji — odstępstwo 4 w `plan.md`.

Pierwszy build solucji po dodaniu projektu pokazał 1 ostrzeżenie `MSB3026` (przejściowa blokada pliku `xunit.abstractions.dll` przez wcześniejszy proces testów — kopiowanie ponowione); build `--no-incremental` po uporządkowaniu `.slnx` — 0 ostrzeżeń.

### Nowe testy

`dotnet test … --logger "console;verbosity=normal"`, `df35353`, wszystkie `Passed` (parametr 31-znakowego klucza zastąpiony placeholderem — hook skanu sekretów):

```text
Api.Tests.ObservationBatchValidatorTests.Validate_TooLongUnitsAndNativeCode_ReturnsIndexedFieldErrors
Api.Tests.ObservationBatchValidatorTests.Validate_UnitsAndNativeCodeWithinLimit_ReturnsNoErrors
Api.Tests.ObservationsApiTests.Post_PayloadWithoutUnitsAndNativeCode_IsAcceptedWithNulls
Api.Tests.ObservationsApiTests.Post_WithUnitsAndNativeCode_StoresAndReturnsThem
Integration.Tests.WorkerToApiTests.Sink_SendsBatch_ApiReturnsSameObservationsWithUnitsAndNativeCode
Integration.Tests.WorkerToApiTests.Synchronizer_ApiUnavailableForTwoRequests_RetriesAndStoresEveryObservationOnce
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_Accepted_PostsContractJsonWithApiKeyAndLogsResult
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_ApiDoesNotAnswer_TimesOutAfterRequestTimeout
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_ApiKeyRejected_LogsErrorAndThrowsSoBatchIsRetried(status: Forbidden)
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_ApiKeyRejected_LogsErrorAndThrowsSoBatchIsRetried(status: Unauthorized)
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_BadRequestWithoutProblemDetails_LogsAndSkipsBatch
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_BaseUrlWithPath_KeepsPath
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_ConnectionFailure_Throws
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_TransientStatus_ThrowsSoBatchIsRetried(status: InternalServerError)
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_TransientStatus_ThrowsSoBatchIsRetried(status: NotFound)
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_TransientStatus_ThrowsSoBatchIsRetried(status: ServiceUnavailable)
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_TransientStatus_ThrowsSoBatchIsRetried(status: TooManyRequests)
Worker.Tests.Api.ApiObservationSinkTests.ReceiveAsync_ValidationProblem_LogsFieldNamesWithoutValuesAndSkipsBatch
Worker.Tests.Api.ObservationBatchMappingTests.ToRequest_EveryCategory_MapsByName(category: Condition, expected: Condition)
Worker.Tests.Api.ObservationBatchMappingTests.ToRequest_EveryCategory_MapsByName(category: Event, expected: Event)
Worker.Tests.Api.ObservationBatchMappingTests.ToRequest_EveryCategory_MapsByName(category: Sample, expected: Sample)
Worker.Tests.Api.ObservationBatchMappingTests.ToRequest_EveryConditionLevel_MapsByName(level: Fault, expected: Fault)
Worker.Tests.Api.ObservationBatchMappingTests.ToRequest_EveryConditionLevel_MapsByName(level: Normal, expected: Normal)
Worker.Tests.Api.ObservationBatchMappingTests.ToRequest_EveryConditionLevel_MapsByName(level: Unavailable, expected: Unavailable)
Worker.Tests.Api.ObservationBatchMappingTests.ToRequest_EveryConditionLevel_MapsByName(level: Warning, expected: Warning)
Worker.Tests.Api.ObservationBatchMappingTests.ToRequest_SampleAndCondition_MapsEveryField
Worker.Tests.WorkerTests.Options_ApiWithKeyOnly_UsesLocalApiDefaults
Worker.Tests.WorkerTests.Services_DefaultRegistration_UsesApiSinkAndHttpAgent
Worker.Tests.WorkerTests.StartAsync_InvalidApiConfiguration_FailsWithConfigurationKeyWithoutValue(setting: "ApiKey", value: "", …)
Worker.Tests.WorkerTests.StartAsync_InvalidApiConfiguration_FailsWithConfigurationKeyWithoutValue(setting: "ApiKey", value: <31 znaków>, …)
Worker.Tests.WorkerTests.StartAsync_InvalidApiConfiguration_FailsWithConfigurationKeyWithoutValue(setting: "ApiKey", value: null, …)
Worker.Tests.WorkerTests.StartAsync_InvalidApiConfiguration_FailsWithConfigurationKeyWithoutValue(setting: "BaseUrl", value: "/relative", …)
Worker.Tests.WorkerTests.StartAsync_InvalidApiConfiguration_FailsWithConfigurationKeyWithoutValue(setting: "BaseUrl", value: "ftp://api:5100", …)
Worker.Tests.WorkerTests.StartAsync_InvalidApiConfiguration_FailsWithConfigurationKeyWithoutValue(setting: "RequestTimeout", value: "00:00:00", …)
```

### Test ręczny (AC-06)

Procesy z `bin/Debug` (`df35353`): symulator `--seed 42` (200 s), API (`ASPNETCORE_URLS=http://localhost:5100`, klucz w `DataAcquisition__Api__ApiKey`), worker (`timeout 150`, ten sam klucz w zmiennej). API zatrzymane (`Stop-Process`) po 60 s, ponownie uruchomione po 20 s (dane API w pamięci — zaczyna od zera).

```text
API 1 up: Healthy
08:39:50
API 1 stopped
API 2 up: Healthy
08:40:12
```

Odczyt wszystkich obserwacji z API po zakończeniu (skrypt Python, `GET /api/v1/observations` oknami 2 s, żadne okno nie było `truncated`):

```text
API: obserwacji 8887 unikalnych 8887 zakres 6168 - 15054 instancje {1793864329} luki [] liczba luk 0 units ['CELSIUS', 'DEGREE', 'MILLIMETER', 'MILLIMETER/SECOND'] nativeCode ['AL-0304']
klucz w logach: api1=0 api2=0 worker=0
```

Analiza logu workera (linie `Sent observations …`):

```text
poziomy: {'info': 132, 'warn': 3, 'fail': 0}
przed awaria: paczek 59 accepted 5807
po restarcie API: paczek 68 accepted 8887 duplicates 0 zakres 6168 - 15054
warn | MTConnect agent http://localhost:5000/ cycle failed (HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5100)); retrying in 00:00:01
warn | MTConnect agent http://localhost:5000/ cycle failed (HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5100)); retrying in 00:00:02
warn | MTConnect agent http://localhost:5000/ cycle failed (HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5100)); retrying in 00:00:04
   info | Sent observations 6068-6167 of VMC5AX (instance 1793864329, Sample): 100 accepted, 0 duplicates.
   info | Sent observations 6168-7167 of VMC5AX (instance 1793864329, Sample): 1000 accepted, 0 duplicates.
   info | Sent observations 7168-8167 of VMC5AX (instance 1793864329, Sample): 1000 accepted, 0 duplicates.
   info | Sent observations 8168-8308 of VMC5AX (instance 1793864329, Sample): 141 accepted, 0 duplicates.
```

Wnioski: suma `accepted` po restarcie API (8887) = liczba obserwacji w API (8887), ten sam zakres 6168–15054, 0 luk, 0 duplikatów. Paczka odrzucona w trakcie przerwy (od 6168) została ponowiona, potem worker nadrobił zaległości paczkami po 1000 (doganianie). Przed przerwą 5807 = migawka 27 + sekwencje 388–6167 (5780). Dane sprzed restartu API przepadły razem z procesem API (store w pamięci — ryzyko opisane w spec).

## Pominięcia i ograniczenia

- **Mylący komunikat ostrzeżenia** (obserwacja z testu ręcznego): synchronizator loguje „MTConnect agent http://localhost:5000/ cycle failed …” także wtedy, gdy zawiódł odbiorca (API na `localhost:5100`) — treść wyjątku wskazuje właściwy adres, ale prefiks sugeruje agenta. `Worker/MTConnect/MTConnectSynchronizer.cs` jest poza zakresem planu DA-004 — propozycja na kolejny temat.
- **Odpowiedź 400 na żywo** nie wystąpiła w teście ręcznym (worker wysyła poprawne paczki); zachowanie sprawdzone testami z fałszywym handlerem.
- **401/403 na żywo** (zły klucz) nie testowane ręcznie — tylko testy jednostkowe.
- **Timeout API** tylko w teście jednostkowym (1 s, realny `HttpClient.Timeout`).
- **Restart API kasuje dane** (store w pamięci, DA-001) — AC-06 porównuje dane od restartu API.
- W katalogu wyjściowym `Integration.Tests` jeden `appsettings.json` nadpisuje drugi (Api/Worker) — bez wpływu na testy (odstępstwo 4).

## Podsumowanie i Review

- **AI review**: [review.md](review.md) — status, przejrzany commit, liczba uwag P1/P2/P3 i sposób ich rozwiązania.
- **Decyzja człowieka**: w [pr.md](pr.md), sekcja *Decyzja*. Agent, który napisał kod, nie może tej zmiany zatwierdzić.
