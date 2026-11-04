# Weryfikacja: DA-001

Status: verified. Commit: d1d9f5d. Środowisko: Windows 11 Pro, .NET SDK 10.0.401, ASP.NET Core runtime 10.0.12; testy integracyjne na `TestServer` (środowisko `Testing`, klucz API losowy per przebieg); test ręczny — `dotnet run` profil `http` (Development), klucz przez zmienną środowiskową (losowy, nie zapisany). Data: 2026-11-04.

## Wyniki testów vs Kryteria Odbioru

| AC | Test / Kroki weryfikacji | Oczekiwany wynik | Wynik rzeczywisty | Dowód (log / output / zrzut) |
|---|---|---|---|---|
| AC-01 | `dotnet build DataAcquisition.slnx`; `dotnet test DataAcquisition.slnx`; `dotnet format DataAcquisition.slnx --verify-no-changes` | Kod 0; solucja z `Contracts`, `Api`, `Api.Tests`; testy workera zielone | PASS | [Build](#build), [Testy — podsumowanie](#testy--podsumowanie), [Format](#format) |
| AC-02 | `ObservationsApiTests.Post_ValidBatchWithSampleAndCondition_AcceptsAndReturnsFromQuery`, `Post_ValidBatch_UsesCamelCaseAndStringEnumsOnWire`; test ręczny POST/GET | `200 {accepted:2, duplicates:0}`; GET zwraca obie obserwacje 1:1, czas w UTC | PASS | [Lista testów](#lista-testów), [Test ręczny](#test-ręczny-curl) |
| AC-03 | `Post_WithoutApiKey_Returns401AndStoresNothing`, `Post_WithInvalidApiKey_Returns401AndStoresNothing`, `Post_WithoutApiKeyAndInvalidBody_Returns401NotValidationError`, `Get_WithoutApiKey_Returns401`; test ręczny POST bez klucza | `401` `application/problem+json`; nic nie zapisane | PASS | [Lista testów](#lista-testów), [Test ręczny](#test-ręczny-curl) |
| AC-04 | `Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing` (8 przypadków: pusta lista, brak `deviceId`, pusty `dataItemId`, `sequence 0`, `Condition` bez poziomu, `Sample` z poziomem, duplikat `sequence`, 1 poprawna + 1 błędna), `Post_MoreThanMaxBatchSize_…` (1001), `Post_InvalidObservationField_ErrorKeyPointsToField`, `Post_MalformedJson_…` (zły JSON, zły enum, zła data), `Post_NumericEnumValue_Returns400AndStoresNothing` (5 przypadków: `category` 7 / 0 / "7", `conditionLevel` 42 / 1 — review R-01), `Post_NonJsonContentType_Returns415AndStoresNothing` (`text/plain`, brak nagłówka — review R-02, spec: `415`) + 30 przypadków `ObservationBatchValidatorTests` (w tym `Validate_UndefinedCategory_…`, `Validate_UndefinedConditionLevel_…`) | `400` z `errors` wskazującym pole (`observations[3].dataItemId`); dla złego JSON i enumów liczbowych `ProblemDetails`; zły `Content-Type` → `415` `ProblemDetails`; nic nie zapisane | PASS | [Lista testów](#lista-testów), [Poprawki po review](#poprawki-po-review-1-commit-d1d9f5d) |
| AC-05 | `Post_SameBatchTwice_SecondReportsDuplicatesOnly`, `Post_OverlappingBatch_AcceptsOnlyNewObservations`, `Post_ParallelIdenticalBatches_StoresEachObservationOnce` (4 × 200), `InMemoryObservationStoreTests.AddAsync_*` (w tym 8 równoległych × 500); test ręczny powtórnego POST | `{0, N}`; `{1, 2}`; suma `accepted` = liczba unikalnych; brak duplikatów w GET | PASS | [Lista testów](#lista-testów), [Test ręczny](#test-ręczny-curl) |
| AC-06 | `Get_FilterByDeviceTimeRangeAndLimit_ReturnsSortedPageWithTruncatedFlag`, `Get_InvalidParameters_Returns400WithFieldError` (5 przypadków), `Get_UnknownDevice_Returns200WithEmptyList`, `InMemoryObservationStoreTests.QueryAsync_*`; test ręczny GET bez `deviceId` | Filtr urządzenia i `[from, to)`, sortowanie (`instanceId`, `sequence`), `limit`, `truncated`; błędne parametry `400`; nieznane urządzenie `200` puste | PASS | [Lista testów](#lista-testów), [Test ręczny](#test-ręczny-curl) |
| AC-07 | `HealthAndStartupTests.GetHealth_WithoutApiKey_Returns200Healthy`; test ręczny `/health` | `200`, `Healthy` | PASS | [Lista testów](#lista-testów), [Test ręczny](#test-ręczny-curl) |
| AC-08 | `Startup_MissingOrTooShortApiKey_FailsWithConfigurationKeyInMessage` (null, pusty, 31 znaków), `Startup_ApiKeyOfMinimumLength_Starts`, `Requests_WithValidAndInvalidKeys_NeverLogKeyValues`, `AddAsync_DuplicateWithDifferentContent_LogsWarningWithoutValue`; test ręczny: start ze starą nazwą zmiennej, `grep` klucza w logu procesu | Start przerwany `OptionsValidationException` z `DataAcquisition:Api:ApiKey`; klucz nigdzie w logach ani w repozytorium | PASS (zastrzeżenie: treści `Api/appsettings*.json` agent nie może odczytać — patrz Pominięcia) | [Lista testów](#lista-testów), [Test ręczny po zmianie nazwy](#test-ręczny-po-zmianie-nazwy-klucza-i-dodaniu-appsettings-commit-d728000), [Sekrety w repo](#sekrety-w-repozytorium) |

## Dowody

### Build

```text
$ dotnet build DataAcquisition.slnx --nologo
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.67
exit=0
```

### Testy — podsumowanie

```text
$ dotnet test DataAcquisition.slnx --nologo --no-build
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 117 ms - DataAcquisition.Worker.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    73, Skipped:     0, Total:    73, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
exit=0
```

Ponownie po zmianie nazwy klucza i dodaniu `appsettings` (drzewo = commit `d728000`):

```text
$ dotnet build DataAcquisition.slnx --nologo
    0 Warning(s)
    0 Error(s)
BUILD_EXIT=0
$ dotnet test DataAcquisition.slnx --nologo --no-build
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 127 ms - DataAcquisition.Worker.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    73, Skipped:     0, Total:    73, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
TEST_EXIT=0
$ dotnet format DataAcquisition.slnx --verify-no-changes
FORMAT_EXIT=0
```

Przebieg czerwony przed implementacją (walidator i store jako `NotImplementedException`, drzewo robocze przed commitem `873fe38`):

```text
Failed!  - Failed:    61, Passed:    12, Skipped:     0, Total:    73, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
     38 System.NotImplementedException : The method or operation is not implemented.
     20 Assert.Equal() Failure: Values differ
```

### Poprawki po review 1 (commit d1d9f5d)

Nowe testy przed poprawką R-01 (drzewo robocze: testy dodane, kod jeszcze z `8344188`):

```text
  Failed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_UndefinedConditionLevel_ReturnsIndexedFieldError
  Failed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_UndefinedCategory_ReturnsIndexedFieldError
  Failed DataAcquisition.Api.Tests.ObservationsApiTests.Post_NumericEnumValue_Returns400AndStoresNothing(body: "{\"deviceId\":\"VMC5AX\",\"instanceId\":1,\"observations\""···)   (×5)
Failed!  - Failed:     7, Passed:    75, Skipped:     0, Total:    82, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
```

(`Post_NonJsonContentType_Returns415AndStoresNothing` przeszedł już przed poprawką — `415` to zachowanie frameworka; R-02 rozwiązane zmianą spec.)

Po poprawce, drzewo = `d1d9f5d`:

```text
$ dotnet build DataAcquisition.slnx --nologo
    0 Warning(s)
    0 Error(s)
BUILD_EXIT=0
$ dotnet test DataAcquisition.slnx --nologo --no-build
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 92 ms - DataAcquisition.Worker.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    82, Skipped:     0, Total:    82, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
TEST_EXIT=0
$ dotnet format DataAcquisition.slnx --verify-no-changes
FORMAT_EXIT=0
$ dotnet test … --logger "console;verbosity=normal"   (nowe testy)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_UndefinedConditionLevel_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_UndefinedCategory_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_NumericEnumValue_Returns400AndStoresNothing(body: "{\"deviceId\":\"VMC5AX\",\"instanceId\":1,\"observations\""···)   (×5)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_NonJsonContentType_Returns415AndStoresNothing(mediaType: "text/plain")
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_NonJsonContentType_Returns415AndStoresNothing(mediaType: null)
$ dotnet publish Api/DataAcquisition.Api.csproj -c Release -o publish/api --nologo   (komenda z release.md; katalog usunięty po sprawdzeniu)
  DataAcquisition.Api -> C:\Users\nog_programista2\Projects\DataAcquisition\publish\api\
PUBLISH_EXIT=0
```

### Lista testów

Lista poniżej pochodzi z przebiegu na `40df9fc` (73 testy Api); 9 testów dodanych po review wymieniono w sekcji wyżej.

Wynik `dotnet test DataAcquisition.slnx --nologo --no-build --logger "console;verbosity=normal"`, posortowany, bez czasów. Parametry niektórych przypadków skrócone (`…`) — pełna treść w plikach testów; wartość parametru 31-znakowego klucza zastąpiona placeholderem (hook skanu sekretów).

```text
Passed DataAcquisition.Api.Tests.HealthAndStartupTests.GetHealth_WithoutApiKey_Returns200Healthy
Passed DataAcquisition.Api.Tests.HealthAndStartupTests.Requests_WithValidAndInvalidKeys_NeverLogKeyValues
Passed DataAcquisition.Api.Tests.HealthAndStartupTests.Startup_ApiKeyOfMinimumLength_Starts
Passed DataAcquisition.Api.Tests.HealthAndStartupTests.Startup_MissingOrTooShortApiKey_FailsWithConfigurationKeyInMessage(apiKey: "")
Passed DataAcquisition.Api.Tests.HealthAndStartupTests.Startup_MissingOrTooShortApiKey_FailsWithConfigurationKeyInMessage(apiKey: <31 znaków>)
Passed DataAcquisition.Api.Tests.HealthAndStartupTests.Startup_MissingOrTooShortApiKey_FailsWithConfigurationKeyInMessage(apiKey: null)
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.AddAsync_ConcurrentIdenticalBatches_StoresEachObservationOnce
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.AddAsync_DuplicateWithDifferentContent_LogsWarningWithoutValue
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.AddAsync_NewObservations_AcceptsAll
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.AddAsync_SameKeysTwice_CountsDuplicatesAndKeepsFirst
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.AddAsync_SameSequenceInOtherInstanceOrDevice_AcceptsAsDistinct
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.QueryAsync_ExactlyLimitMatches_IsNotTruncated
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.QueryAsync_MoreMatchesThanLimit_ReturnsLimitAndTruncated
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.QueryAsync_MultipleDevicesAndInstances_FiltersByDeviceAndSortsByInstanceThenSequence
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.QueryAsync_TimeRange_IncludesFromAndExcludesTo
Passed DataAcquisition.Api.Tests.InMemoryObservationStoreTests.QueryAsync_UnknownDevice_ReturnsEmpty
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_ConditionWithoutLevel_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_DuplicateSequenceInBatch_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_EmptyObservations_ReturnsObservationsError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_InvalidInstanceId_ReturnsInstanceIdError(instanceId: -1)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_InvalidInstanceId_ReturnsInstanceIdError(instanceId: 0)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_InvalidInstanceId_ReturnsInstanceIdError(instanceId: null)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_InvalidSequence_ReturnsIndexedFieldError(sequence: 0)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_InvalidSequence_ReturnsIndexedFieldError(sequence: null)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MaxBatchSize_ReturnsNoErrors
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MissingCategory_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MissingDataItemId_ReturnsIndexedFieldError(dataItemId: "")
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MissingDataItemId_ReturnsIndexedFieldError(dataItemId: null)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MissingDeviceId_ReturnsDeviceIdError(deviceId: "   ")
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MissingDeviceId_ReturnsDeviceIdError(deviceId: "")
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MissingDeviceId_ReturnsDeviceIdError(deviceId: null)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MissingTimestamp_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MissingValue_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_MoreThanMaxBatchSize_ReturnsObservationsError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_NonConditionWithLevel_ReturnsIndexedFieldError(category: Event)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_NonConditionWithLevel_ReturnsIndexedFieldError(category: Sample)
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_NullObservationElement_ReturnsIndexedError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_NullObservations_ReturnsObservationsError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_TooLongDataItemId_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_TooLongDeviceId_ReturnsDeviceIdError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_TooLongTypeAndSubType_ReturnsIndexedFieldErrors
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_TooLongValue_ReturnsIndexedFieldError
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_UnavailableValue_ReturnsNoErrors
Passed DataAcquisition.Api.Tests.ObservationBatchValidatorTests.Validate_ValidBatchWithSampleAndCondition_ReturnsNoErrors
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Get_FilterByDeviceTimeRangeAndLimit_ReturnsSortedPageWithTruncatedFlag
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Get_InvalidParameters_Returns400WithFieldError(query: "", field: "deviceId")
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Get_InvalidParameters_Returns400WithFieldError(query: "?deviceId=VMC5AX&from=2026-11-04T10:00:00Z&to=2026"···, field: "from")
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Get_InvalidParameters_Returns400WithFieldError(query: "?deviceId=VMC5AX&from=2026-11-04T11:00:00Z&to=2026"···, field: "from")
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Get_InvalidParameters_Returns400WithFieldError(query: "?deviceId=VMC5AX&limit=0", field: "limit")
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Get_InvalidParameters_Returns400WithFieldError(query: "?deviceId=VMC5AX&limit=1001", field: "limit")
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Get_UnknownDevice_Returns200WithEmptyList
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Get_WithoutApiKey_Returns401
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(caseName: "condition without level", …)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(caseName: "duplicate sequence", …)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(caseName: "empty dataItemId", …)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(caseName: "empty list", …)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(caseName: "missing deviceId", …)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(caseName: "one valid one invalid", …)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(caseName: "sample with level", …)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(caseName: "sequence 0", …)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_InvalidObservationField_ErrorKeyPointsToField
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_MalformedJson_Returns400ProblemDetailsAndStoresNothing(body: "{ not json")
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_MalformedJson_Returns400ProblemDetailsAndStoresNothing(body: …"category":"Bogus"…)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_MalformedJson_Returns400ProblemDetailsAndStoresNothing(body: …"timestamp":"not-a-date"…)
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_MoreThanMaxBatchSize_Returns400AndStoresNothing
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_OverlappingBatch_AcceptsOnlyNewObservations
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_ParallelIdenticalBatches_StoresEachObservationOnce
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_SameBatchTwice_SecondReportsDuplicatesOnly
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_ValidBatchWithSampleAndCondition_AcceptsAndReturnsFromQuery
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_ValidBatch_UsesCamelCaseAndStringEnumsOnWire
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_WithInvalidApiKey_Returns401AndStoresNothing
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_WithoutApiKeyAndInvalidBody_Returns401NotValidationError
Passed DataAcquisition.Api.Tests.ObservationsApiTests.Post_WithoutApiKey_Returns401AndStoresNothing
Passed DataAcquisition.Worker.Tests.WorkerTests.ExecuteAsync_AfterStartAndOneSecond_LogsTwice
Passed DataAcquisition.Worker.Tests.WorkerTests.ExecuteAsync_BeforeOneSecondElapsed_LogsOnce
Passed DataAcquisition.Worker.Tests.WorkerTests.StopAsync_WhenRunning_CompletesWithoutExceptionAndStopsLogging
```

### Format

```text
$ dotnet format DataAcquisition.slnx --verify-no-changes
format exit=0
```

### Test ręczny (curl)

Klucz API ustawiony w zmiennej środowiskowej na losową wartość (`secrets.token_hex(24)`), `dotnet run --project Api --no-build --launch-profile http`. `traceId` zastąpione `<traceId>`.

```text
--- health
Healthy [200]
--- POST bez klucza
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Unauthorized","status":401,"traceId":"<traceId>"} [401]
--- POST
{"accepted":1,"duplicates":0} [200]
--- POST ponownie
{"accepted":0,"duplicates":1} [200]
--- GET
{"items":[{"deviceId":"VMC5AX","instanceId":1793790000,"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30.12+00:00","category":"Sample","type":"POSITION","subType":"ACTUAL","value":"112.457","conditionLevel":null}],"truncated":false} [200]
--- GET bez deviceId
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"deviceId":["deviceId is required."]},"traceId":"<traceId>"} [400]
--- log zawiera klucz? (grep -c)
0
```

(Powyższy przebieg wykonano na `40df9fc` — przed zmianą nazwy klucza i bez `appsettings`.)

### Test ręczny po zmianie nazwy klucza i dodaniu appsettings (commit d728000)

Klucz losowy `daq-api-` + `secrets.token_hex(24)`, tylko w zmiennej środowiskowej procesu.

```text
=== stara nazwa ApiKey__Value (oczekiwany brak startu)
      Microsoft.Extensions.Options.OptionsValidationException: Configuration 'DataAcquisition:Api:ApiKey' is required and must be at least 32 characters long.
Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException: Configuration 'DataAcquisition:Api:ApiKey' is required and must be at least 32 characters long.
=== nowa nazwa (DataAcquisition__Api__ApiKey)
Healthy [200]
{"accepted":1,"duplicates":0} [200]
bez klucza [401]
=== log procesu
Using launch settings from Api\Properties\launchSettings.json...
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5100
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Development
info: Microsoft.Hosting.Lifetime[0]
      Content root path: C:\Users\nog_programista2\Projects\DataAcquisition\Api
dbug: DataAcquisition.Api.ApiKey.ApiKeyAuthenticationHandler[9]
      AuthenticationScheme: ApiKey was not authenticated.
dbug: DataAcquisition.Api.ApiKey.ApiKeyAuthenticationHandler[9]
      AuthenticationScheme: ApiKey was not authenticated.
dbug: DataAcquisition.Api.ApiKey.ApiKeyAuthenticationHandler[8]
      AuthenticationScheme: ApiKey was successfully authenticated.
info: DataAcquisition.Api.Observations.ObservationEndpoints[0]
      Observation batch from VMC5AX (instance 1): 1 accepted, 0 duplicates.
dbug: DataAcquisition.Api.ApiKey.ApiKeyAuthenticationHandler[9]
      AuthenticationScheme: ApiKey was not authenticated.
warn: DataAcquisition.Api.ApiKey.ApiKeyAuthenticationHandler[0]
      Rejected GET /api/v1/observations: missing or invalid API key.
info: DataAcquisition.Api.ApiKey.ApiKeyAuthenticationHandler[12]
      AuthenticationScheme: ApiKey was challenged.
=== klucz w logu: 0
```

Wnioski: stara nazwa zmiennej nie jest już czytana; `appsettings` działają — brak logów `Microsoft.AspNetCore.*` na `Information` (por. poprzedni przebieg), w Development widoczne `dbug` dla `DataAcquisition.*`.

### Sekrety w repozytorium

Pliki `Api/appsettings.json` i `Api/appsettings.Development.json` utworzył człowiek; agent ma regułę odmowy odczytu `**/appsettings*.json`, więc **nie zweryfikował ich treści** (brak klucza w nich potwierdza pośrednio start ze starą nazwą zmiennej — gdyby `appsettings` zawierały `DataAcquisition:Api:ApiKey`, API by wystartowało).

Klucz w testach: `ApiFactory.TestApiKey` generowany losowo w czasie wykonania (brak literału w kodzie).

## Pominięcia i ograniczenia

- **Treść `Api/appsettings*.json` niezweryfikowana przez agenta** (reguła odmowy odczytu) — do przejrzenia przez człowieka/recenzenta w diffie.
- **`timestamp` bez przesunięcia nie jest odrzucany** (odstępstwo 2 w `plan.md`) — brak testu na ten przypadek, bo zachowanie nie jest zaimplementowane.
- Nie testowano obciążeniowo ani długotrwałej pracy (wzrost pamięci store'a — ryzyko zaakceptowane w spec).
- Nie testowano integracji z realnym workerem (worker poza zakresem DA-001) ani z symulatorem MTConnect.
- Test ręczny wykonany na tym samym komputerze, HTTP bez TLS (HTTPS poza zakresem).

## Podsumowanie i Review

- **AI review**: [review.md](review.md) — status, przejrzany commit, liczba uwag P1/P2/P3 i sposób ich rozwiązania.
- **Decyzja człowieka**: w [pr.md](pr.md), sekcja *Decyzja*. Agent, który napisał kod, nie może tej zmiany zatwierdzić.
