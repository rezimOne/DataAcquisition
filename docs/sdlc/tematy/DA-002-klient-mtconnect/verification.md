# Weryfikacja: DA-002

Status: verified. Commit: d83768a. Środowisko: Windows 11 Pro, .NET SDK 10.0.401, runtime 10.0.12, Python 3.14.7 (symulator `resources/cnc_mtconnect_sim.py --seed 42`, 5 Hz); worker uruchomiony z `DOTNET_ENVIRONMENT=Development`, konfiguracja domyślna (`http://localhost:5000`). Bez sekretów. Data: 2026-11-04.

## Wyniki testów vs Kryteria Odbioru

| AC | Test / Kroki weryfikacji | Oczekiwany wynik | Wynik rzeczywisty | Dowód (log / output / zrzut) |
|---|---|---|---|---|
| AC-01 | `dotnet build` / `dotnet test` / `dotnet format --verify-no-changes` dla `DataAcquisition.slnx` | Kod 0 | PASS | [Build, testy, format](#build-testy-format) |
| AC-02 | `MTConnectParserTests` (15 przypadków) na fixtures z symulatora (`probe`, `current`, `sample`, `error-out-of-range`) i ręcznych (`sample-conditions`, `sample-empty`, `streams-foreign-namespace`, `malformed`) | Pola modelu, sortowanie po `sequence`, nagłówek, UTC; błędne dokumenty → `MTConnectFormatException` | PASS | [Lista testów](#lista-testów-workera) |
| AC-03 | `StepAsync_FirstStep_…`, `StepAsync_ContinuousSampling_…`, `StepAsync_AgentHasMoreData_ReturnsZeroDelayToCatchUp`, `StepAsync_ObservationsOfTwoDevices_…`, `StepAsync_KnownDataItem_…`, `StepAsync_UnknownDataItem_…`; klient: `MTConnectClientTests` (URL-e `/probe`, `/current`, `/sample?from=&count=`, `AgentUrl` ze ścieżką); test ręczny | Kolejne `from` = `nextSequence`; każda obserwacja raz, rosnąco; doganianie bez czekania | PASS | [Lista testów](#lista-testów-workera), [Test ręczny](#test-ręczny-z-symulatorem-ac-08) |
| AC-04 | `StepAsync_InstanceIdChanged_LogsAndResynchronizesWithProbe`, `StepAsync_RestartedAgentAnswersOutOfRange_LogsRestartAndReprobes` (regresja z testu ręcznego); test ręczny z restartem symulatora | Log `Information` o restarcie, ponowne `/probe` i `/current`, dalsze `/sample` nowej instancji **od jej `firstSequence`** (po review R-01) | PASS (po poprawkach `96142b5`, `bf41614`) | [Błąd znaleziony w teście ręcznym](#błąd-znaleziony-w-teście-ręcznym), [Po review 1](#po-review-1-r-01--r-03-commit-6538cca) |
| AC-05 | `StepAsync_OutOfRange_ResumesFromBufferWithMarginAndWarnsAboutExactLostRange`, `StepAsync_OutOfRangeWithSmallFullBuffer_KeepsMarginWithinBuffer`, `StepAsync_OutOfRangeWithNotFullBuffer_ResumesFromFirstSequence`; test ręczny `--buffer 1000`, `PollInterval` 30 s | `/current` bez `/probe`; `/sample` od razu z danych w buforze (pełny bufor: zapas `min(SampleCount, bufferSize/10)`); `Warning` z dokładnym zakresem (wartości strukturalne `LostFrom`/`LostTo`); brak pętli `OUT_OF_RANGE` | PASS (po poprawkach `bf41614`, `6538cca`) | [Po review 1](#po-review-1-r-01--r-03-commit-6538cca) |
| AC-06 | `StepAsync_TransientError_…` (5 rodzajów błędów: brak połączenia, timeout, HTTP 500, zły XML, inny błąd agenta), `StepAsync_RepeatedErrors_DoublesDelayUpToMaxAndResetsAfterSuccess` (1, 2, 4, 8, 16, 30, 30, potem 1), `StepAsync_SinkFails_RedeliversSameBatch`, `StepAsync_CurrentFailsAfterProbe_…`; klient: timeout, 500, 404 bez `MTConnectError`, zły XML; test ręczny — symulator wyłączony w trakcie | `Warning`, ten sam `from`, backoff z limitem i resetem, ponowne dostarczenie paczki, host działa | PASS | [Lista testów](#lista-testów-workera), [Test ręczny](#test-ręczny-z-symulatorem-ac-08) |
| AC-07 | `WorkerTests.StartAsync_InvalidConfiguration_FailsWithConfigurationKey` (7 przypadków), `Options_NoConfiguration_UsesSimulatorDefaults`, `StopAsync_WhileRequestInProgress_…`, `RunAsync_StoppedWhileWaiting_…`, `RunAsync_StoppedDuringRequest_…` | `OptionsValidationException` z pełnym kluczem; zatrzymanie bez wyjątku i bez `Warning`/`Error` | PASS | [Lista testów](#lista-testów-workera) |
| AC-08 | Symulator 70 s → przerwa ~10 s → nowy symulator 65 s; worker 150 s; analiza logu skryptem (ciągłość `sequence` w paczkach `Sample`) | Ciągłe zakresy w ramach instancji; po restarcie log zmiany `instanceId` i ciągłość w nowej instancji | PASS (przebieg 2 na `96142b5`; przebieg 6 na `6538cca`) | [Test ręczny](#test-ręczny-z-symulatorem-ac-08), [Po review 1](#po-review-1-r-01--r-03-commit-6538cca) |

## Dowody

### Build, testy, format

Drzewo = `96142b5`:

```text
$ dotnet build DataAcquisition.slnx --nologo
    0 Error(s)
    0 Warning(s)
BUILD_EXIT=0
$ dotnet test DataAcquisition.slnx --nologo --no-build
Passed!  - Failed:     0, Passed:    82, Skipped:     0, Total:    82, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
TEST_EXIT=0
$ dotnet format DataAcquisition.slnx --verify-no-changes
FORMAT_EXIT=0
```

Przebiegi czerwone (test-first, drzewo robocze przed commitami implementacji):

```text
# parser na zaślepce (przed 2ffd2f2)
Failed!  - Failed:    15, Passed:     3, Skipped:     0, Total:    18, Duration: 324 ms - DataAcquisition.Worker.Tests.dll (net10.0)
# synchronizator na zaślepce (przed c5cbe4a)
Failed!  - Failed:    18, Passed:    18, Skipped:     0, Total:    36, Duration: 132 ms - DataAcquisition.Worker.Tests.dll (net10.0)
# test reprodukujący błąd z testu ręcznego (commit 3cb00ba, przed poprawką 96142b5)
  Failed DataAcquisition.Worker.Tests.MTConnect.MTConnectSynchronizerTests.StepAsync_RestartedAgentAnswersOutOfRange_LogsRestartAndReprobes [5 ms]
   Assert.Equal() Failure: Collections differ at index 2
Expected: "probe"
Actual:   "sample?from=28&count=1000"
Failed!  - Failed:     1, Passed:    51, Skipped:     0, Total:    52, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
```

Uwaga: przy pierwszym sprawdzeniu formatu wynik `FORMAT_EXIT=0` pochodził z `tail` w potoku, a `dotnet format` zgłaszał białe znaki w `MTConnectServiceCollectionExtensions.cs`; poprawione formaterem przed commitem `c5cbe4a`. Powyższe kody wyjścia są mierzone bez potoku.

### Lista testów workera

`dotnet test DataAcquisition.slnx --nologo --no-build --logger "console;verbosity=normal"`, wszystkie `Passed`, posortowane, prefiks `DataAcquisition.Worker.Tests.` pominięty, długie parametry obcięte:

```text
MTConnect.MTConnectClientTests.CurrentAsync_AgentDoesNotAnswer_TimesOutAfterRequestTimeout
MTConnect.MTConnectClientTests.CurrentAsync_OkWithMalformedXml_ThrowsFormatException
MTConnect.MTConnectClientTests.CurrentAsync_Ok_RequestsCurrent
MTConnect.MTConnectClientTests.ProbeAsync_DefaultAgentUrl_RequestsProbeAndParsesDevices
MTConnect.MTConnectClientTests.SampleAsync_AgentUrlWithPath_KeepsPathAndSendsFromAndCount
MTConnect.MTConnectClientTests.SampleAsync_ErrorStatusWithoutMTConnectError_ThrowsHttpRequestException(status: InternalServerError, body: "oops")
MTConnect.MTConnectClientTests.SampleAsync_ErrorStatusWithoutMTConnectError_ThrowsHttpRequestException(status: NotFound, body: "<html>not found</html>")
MTConnect.MTConnectClientTests.SampleAsync_OutOfRange404_ThrowsAgentErrorWithCode
MTConnect.MTConnectParserTests.ParseDevices_SimulatorProbe_ReturnsDeviceAndDataItems
MTConnect.MTConnectParserTests.ParseDevices_StreamsDocument_ThrowsFormatException
MTConnect.MTConnectParserTests.ParseStreams_ConditionsAndUnavailable_MapsLevelNativeCodeAndRawValue
MTConnect.MTConnectParserTests.ParseStreams_EmptySample_ReturnsHeaderAndNoObservations
MTConnect.MTConnectParserTests.ParseStreams_MissingHeaderAttribute_ThrowsFormatException
MTConnect.MTConnectParserTests.ParseStreams_SimulatorCurrent_ReturnsHeaderAndObservationsSortedBySequence
MTConnect.MTConnectParserTests.ParseStreams_SimulatorSample_MapsCategoryTypeAndValueFromElements
MTConnect.MTConnectParserTests.ParseStreams_SimulatorSample_ReturnsConsecutiveSequencesAndCatchUpHeader
MTConnect.MTConnectParserTests.ParseStreams_UnsupportedDocument_ThrowsFormatException(fixture: "error-out-of-range.xml")
MTConnect.MTConnectParserTests.ParseStreams_UnsupportedDocument_ThrowsFormatException(fixture: "malformed.xml")
MTConnect.MTConnectParserTests.ParseStreams_UnsupportedDocument_ThrowsFormatException(fixture: "probe.xml")
MTConnect.MTConnectParserTests.ParseStreams_UnsupportedDocument_ThrowsFormatException(fixture: "streams-foreign-namespace.xml")
MTConnect.MTConnectParserTests.TryParseError_NotAnErrorDocument_ReturnsNull(fixture: "current.xml")
MTConnect.MTConnectParserTests.TryParseError_NotAnErrorDocument_ReturnsNull(fixture: "malformed.xml")
MTConnect.MTConnectParserTests.TryParseError_OutOfRange_ReturnsErrorCode
MTConnect.MTConnectSynchronizerTests.RunAsync_StoppedDuringRequest_CompletesWithoutWarnings
MTConnect.MTConnectSynchronizerTests.RunAsync_StoppedWhileWaiting_CompletesWithoutWarnings
MTConnect.MTConnectSynchronizerTests.StepAsync_AgentHasMoreData_ReturnsZeroDelayToCatchUp
MTConnect.MTConnectSynchronizerTests.StepAsync_ContinuousSampling_UsesNextSequenceAndDeliversEachObservationOnceInOrder
MTConnect.MTConnectSynchronizerTests.StepAsync_CurrentFailsAfterProbe_RetriesCurrentWithoutProbe
MTConnect.MTConnectSynchronizerTests.StepAsync_FirstStep_ProbesThenDeliversCurrentSnapshot
MTConnect.MTConnectSynchronizerTests.StepAsync_InstanceIdChanged_LogsAndResynchronizesWithProbe
MTConnect.MTConnectSynchronizerTests.StepAsync_KnownDataItem_TakesTypeSubTypeAndUnitsFromProbe
MTConnect.MTConnectSynchronizerTests.StepAsync_ObservationsOfTwoDevices_DeliversOneBatchPerDevice
MTConnect.MTConnectSynchronizerTests.StepAsync_OutOfRange_ResynchronizesWithoutProbeAndWarnsAboutLostRange
MTConnect.MTConnectSynchronizerTests.StepAsync_RepeatedErrors_DoublesDelayUpToMaxAndResetsAfterSuccess
MTConnect.MTConnectSynchronizerTests.StepAsync_RestartedAgentAnswersOutOfRange_LogsRestartAndReprobes
MTConnect.MTConnectSynchronizerTests.StepAsync_SinkFails_RedeliversSameBatch
MTConnect.MTConnectSynchronizerTests.StepAsync_TransientError_WarnsRetriesSameFromAndKeepsRunning(error: …MTConnectAgentErrorException: MTConnect agent error UNSUPPORTED…)
MTConnect.MTConnectSynchronizerTests.StepAsync_TransientError_WarnsRetriesSameFromAndKeepsRunning(error: …MTConnectFormatException: Response is not well-formed XML.)
MTConnect.MTConnectSynchronizerTests.StepAsync_TransientError_WarnsRetriesSameFromAndKeepsRunning(error: …HttpRequestException: MTConnect agent returned HTTP 500.)
MTConnect.MTConnectSynchronizerTests.StepAsync_TransientError_WarnsRetriesSameFromAndKeepsRunning(error: …HttpRequestException: No connection could be made.)
MTConnect.MTConnectSynchronizerTests.StepAsync_TransientError_WarnsRetriesSameFromAndKeepsRunning(error: …TaskCanceledException: …HttpClient.Timeout.)
MTConnect.MTConnectSynchronizerTests.StepAsync_UnknownDataItem_KeepsElementTypeAndWarnsOnce
WorkerTests.Options_NoConfiguration_UsesSimulatorDefaults
WorkerTests.Services_DefaultRegistration_UsesLoggingSinkAndHttpAgent
WorkerTests.StartAsync_InvalidConfiguration_FailsWithConfigurationKey(setting: "AgentUrl", value: "/relative", …)
WorkerTests.StartAsync_InvalidConfiguration_FailsWithConfigurationKey(setting: "AgentUrl", value: "ftp://agent:5000", …)
WorkerTests.StartAsync_InvalidConfiguration_FailsWithConfigurationKey(setting: "PollInterval", value: "00:00:00", …)
WorkerTests.StartAsync_InvalidConfiguration_FailsWithConfigurationKey(setting: "RequestTimeout", value: "00:10:00", …)
WorkerTests.StartAsync_InvalidConfiguration_FailsWithConfigurationKey(setting: "RetryMaxDelay", value: "00:00:00.500", expectedKey: "…RetryInitialDelay")
WorkerTests.StartAsync_InvalidConfiguration_FailsWithConfigurationKey(setting: "SampleCount", value: "0", …)
WorkerTests.StartAsync_InvalidConfiguration_FailsWithConfigurationKey(setting: "SampleCount", value: "1001", …)
WorkerTests.StopAsync_WhileRequestInProgress_CompletesWithoutWarningsOrErrors
```

### Błąd znaleziony w teście ręcznym

Przebieg 1 (commit `c5cbe4a`, symulatory `instanceId=1793793096` → `1793793176`): ciągłość OK (128 paczek `Sample`, 0 luk), ale po restarcie symulatora **brak logu o restarcie i brak ponownego `/probe`**:

```text
info | MTConnect agent http://localhost:5000/ (instance 1793793096): 1 device(s), 27 data items.
info | Synchronized with MTConnect agent instance 1793793096 at sequence 268.
warn | MTConnect agent http://localhost:5000/ cycle failed (HttpRequestException: An error occurred while sending the request.); retrying in 00:00:01.
warn | MTConnect agent http://localhost:5000/ cycle failed (HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5000)); retrying in 00:00:02.
info | Synchronized with MTConnect agent instance 1793793176 at sequence 28.
```

Przyczyna: zrestartowany agent ma niższe sekwencje, więc `/sample?from=<stary nextSequence>` zwraca `OUT_OF_RANGE` (nie dokument z nowym `instanceId`); ścieżka `OUT_OF_RANGE` robiła resync bez sprawdzenia instancji. Test reprodukujący: `3cb00ba` (czerwony), poprawka: `96142b5` (wykrywanie restartu i ponowne `/probe` w `SynchronizeAsync`, niezależnie od ścieżki) — test bez zmian, zielony.

### Test ręczny z symulatorem (AC-08)

Przebieg 2 (commit `96142b5`): symulator `instanceId=1793793345` (70 s) → przerwa ~10 s → symulator `instanceId=1793793426` (65 s); worker 150 s.

Analiza logu (skrypt Python: każda paczka `Sample` musi zaczynać się od `poprzedni koniec + 1`, a pierwsza po synchronizacji od `nextSequence` z `/current`):

```text
sample batches: 129 | observations per instance: {'1793793345': 6741, '1793793426': 6507} | gaps: 0
levels: Counter({'info': 139, 'warn': 1})
```

Fragment logu (start i restart):

```text
Received 27 observations from VMC5AX (instance 1793793345, Current), sequence 1-287.
Synchronized with MTConnect agent instance 1793793345 at sequence 288.
Received 100 observations from VMC5AX (instance 1793793345, Sample), sequence 288-387.
Received 100 observations from VMC5AX (instance 1793793345, Sample), sequence 388-487.
Received 100 observations from VMC5AX (instance 1793793345, Sample), sequence 488-587.
...
Received 95 observations from VMC5AX (instance 1793793345, Sample), sequence 6831-6925.
Received 76 observations from VMC5AX (instance 1793793345, Sample), sequence 6926-7001.
MTConnect agent http://localhost:5000/ cycle failed (HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5000)); retrying in 00:00:01.
MTConnect agent restarted (instance 1793793345 -> 1793793426); resynchronizing.
MTConnect agent http://localhost:5000/ (instance 1793793426): 1 device(s), 27 data items.
Received 27 observations from VMC5AX (instance 1793793426, Current), sequence 1-27.
Synchronized with MTConnect agent instance 1793793426 at sequence 28.
Received 120 observations from VMC5AX (instance 1793793426, Sample), sequence 28-147.
Received 100 observations from VMC5AX (instance 1793793426, Sample), sequence 148-247.
Received 100 observations from VMC5AX (instance 1793793426, Sample), sequence 248-347.
```

Logi nie zawierają wartości obserwacji ani treści odpowiedzi agenta.

### Po review 1 (R-01 – R-03, commit 6538cca)

R-01 (decyzja człowieka): po resynchronizacji `/sample` wznawia się od danych w buforze agenta. Najpierw zmienione/nowe testy, potem kod:

```text
# przed zmianą synchronizatora (testy R-01/R-03 zmienione, kod z 96142b5 + BufferSize w nagłówku)
  Failed …StepAsync_InstanceIdChanged_LogsAndResynchronizesWithProbe
  Failed …StepAsync_OutOfRangeWithNotFullBuffer_ResumesFromFirstSequence
  Failed …StepAsync_OutOfRange_ResumesFromBufferWithMarginAndWarnsAboutExactLostRange
  Failed …StepAsync_RestartedAgentAnswersOutOfRange_LogsRestartAndReprobes
Failed!  - Failed:     4, Passed:    49, Skipped:     0, Total:    53, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
# po zmianie (commit bf41614): 53/53
```

**Przebieg 4 (ręczny, commit `bf41614`) wykazał 2 błędy poprawki R-01** — symulator `--buffer 1000` (~10 s danych), worker z `DataAcquisition__MTConnect__PollInterval=00:00:30`: 0 paczek `Sample`, worker skakał do `nextSequence`:

```text
sample batches: 0 | observations per instance: {'1793798099': 108} | gaps: 0
warn | Observations 268-3267 of instance 1793798099 left the agent buffer before they were read and are lost.
info | Synchronized with MTConnect agent instance 1793798099 at sequence 3268.
warn | Observations 3268-6267 of instance 1793798099 left the agent buffer before they were read and are lost.
```

Przyczyny: czekanie `PollInterval` po synchronizacji i zapas `SampleCount` (1000) = cały bufor. Testy przed poprawką `6538cca`:

```text
  Failed …StepAsync_ContinuousSampling_UsesNextSequenceAndDeliversEachObservationOnceInOrder
  Failed …StepAsync_FirstStep_ProbesThenDeliversCurrentSnapshot
  Failed …StepAsync_OutOfRangeWithSmallFullBuffer_KeepsMarginWithinBuffer
Failed!  - Failed:     3, Passed:    51, Skipped:     0, Total:    54, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
```

Po poprawce, drzewo = `6538cca`:

```text
$ dotnet build DataAcquisition.slnx --nologo
    0 Error(s)
    0 Warning(s)
BUILD_EXIT=0
$ dotnet test DataAcquisition.slnx --nologo --no-build
Passed!  - Failed:     0, Passed:    82, Skipped:     0, Total:    82, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
TEST_EXIT=0
$ dotnet format DataAcquisition.slnx --verify-no-changes
FORMAT_EXIT=0
$ dotnet publish Worker/DataAcquisition.Worker.csproj -c Release -o publish/worker --nologo   (komenda z release.md, na bf41614; katalog usunięty)
PUBLISH_EXIT=0
```

Nowe testy względem listy wyżej: `StepAsync_OutOfRange_ResumesFromBufferWithMarginAndWarnsAboutExactLostRange` (zastępuje `StepAsync_OutOfRange_ResynchronizesWithoutProbeAndWarnsAboutLostRange`), `StepAsync_OutOfRangeWithSmallFullBuffer_KeepsMarginWithinBuffer`, `StepAsync_OutOfRangeWithNotFullBuffer_ResumesFromFirstSequence`.

**Przebieg 5 (ręczny, `6538cca`)** — `--buffer 1000`, `PollInterval` 30 s (worker celowo wolniejszy niż bufor):

```text
sample batches: 3 | observations per instance: {'1793799192': 2808} | gaps: 0
info | Synchronized with MTConnect agent instance 1793799192 at sequence 288.
warn | Observations 288-2387 of instance 1793799192 left the agent buffer before they were read and are lost.
info | Synchronized with MTConnect agent instance 1793799192 at sequence 2388.
Received 900 observations from VMC5AX (instance 1793799192, Sample), sequence 2388-3287.
warn | Observations 3288-5387 of instance 1793799192 left the agent buffer before they were read and are lost.
info | Synchronized with MTConnect agent instance 1793799192 at sequence 5388.
Received 900 observations from VMC5AX (instance 1793799192, Sample), sequence 5388-6287.
warn | Observations 6288-8349 of instance 1793799192 left the agent buffer before they were read and are lost.
info | Synchronized with MTConnect agent instance 1793799192 at sequence 8350.
Received 900 observations from VMC5AX (instance 1793799192, Sample), sequence 8350-9249.
```

Wniosek: po `OUT_OF_RANGE` worker od razu czyta 90% bufora (zapas 100 = 10% z 1000), luka jest raportowana dokładnie, nie ma pętli `OUT_OF_RANGE`. Utrata ~2100 obserwacji/cykl wynika z ustawienia testu (przerwa 30 s > bufor ~10 s).

**Przebieg 6 (ręczny, `6538cca`)** — restart agenta jak w przebiegu 2:

```text
sample batches: 126 | observations per instance: {'1793799290': 6685, '1793799371': 6515} | gaps: 0
info | Synchronized with MTConnect agent instance 1793799290 at sequence 268.
warn | MTConnect agent http://localhost:5000/ cycle failed (HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5000)); retrying in 00:00:01.
warn | MTConnect agent http://localhost:5000/ cycle failed (HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5000)); retrying in 00:00:02.
info | MTConnect agent restarted (instance 1793799290 -> 1793799371); resynchronizing.
info | MTConnect agent http://localhost:5000/ (instance 1793799371): 1 device(s), 27 data items.
info | Received 27 observations from VMC5AX (instance 1793799371, Current), sequence 1-307.
info | Synchronized with MTConnect agent instance 1793799371 at sequence 1.
```

Po restarcie worker pobiera nową instancję od sekwencji 1 (wcześniej skakał do `nextSequence` i tracił dane zebrane od restartu); 0 luk.

### Po review 2 (R-04)

Test `StepAsync_OutOfRangeRightAfterResumeFromBuffer_FallsBackToNextSequenceInsteadOfLooping` (commit `20da805`, liczba kroków poprawiona przed poprawką kodu — patrz odstępstwo 8) na kodzie sprzed poprawki:

```text
  Failed DataAcquisition.Worker.Tests.MTConnect.MTConnectSynchronizerTests.StepAsync_OutOfRangeRightAfterResumeFromBuffer_FallsBackToNextSequenceInsteadOfLooping
Expected: "sample?from=232572&count=1000"
Actual:   "sample?from=102500&count=1000"
Failed!  - Failed:     1, Passed:    54, Skipped:     0, Total:    55, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
```

Po poprawce, drzewo = `d83768a`:

```text
$ dotnet build DataAcquisition.slnx --nologo
    0 Error(s)
    0 Warning(s)
BUILD_EXIT=0
$ dotnet test DataAcquisition.slnx --nologo --no-build
Passed!  - Failed:     0, Passed:    55, Skipped:     0, Total:    55, Duration: 1 s - DataAcquisition.Worker.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    82, Skipped:     0, Total:    82, Duration: 1 s - DataAcquisition.Api.Tests.dll (net10.0)
TEST_EXIT=0
$ dotnet format DataAcquisition.slnx --verify-no-changes
FORMAT_EXIT=0
```

Scenariusz R-04 nie był odtwarzany ręcznie (wymaga agenta, który między `/current` a `/sample` dopisze > 10% bufora) — tylko test jednostkowy.

## Pominięcia i ograniczenia

- **`OUT_OF_RANGE` przy ciągle działającym agencie** sprawdzony ręcznie tylko z małym buforem (`--buffer 1000`) i sztucznie długim `PollInterval`; scenariusz realny (bufor 131072, przestój > ~22 min) — tylko testem jednostkowym.
- **Niedostępność agenta** sprawdzona ręcznie tylko przez wyłączenie symulatora (odmowa połączenia); timeout i HTTP 500 — tylko testami z fałszywym handlerem/agentem. W przerwie ~10 s wystąpiły 1–2 nieudane próby — odmowa połączenia na `localhost` w Windows trwa kilka sekund (IPv6 + IPv4).
- **Realny agent MTConnect** (np. cppagent) nie był testowany — tylko symulator (MTConnect 1.3); obce wersje namespace są jawnie odrzucane.
- **Ostatni `Warning` w przebiegu 2** to odmowa połączenia po zakończeniu drugiego symulatora (65 s) przed zatrzymaniem workera (150 s) — oczekiwane.
- Worker zatrzymywany przez `timeout` (zakończenie procesu), nie Ctrl+C — łagodne zatrzymanie pokryte testami AC-07.

## Podsumowanie i Review

- **AI review**: [review.md](review.md) — status, przejrzany commit, liczba uwag P1/P2/P3 i sposób ich rozwiązania.
- **Decyzja człowieka**: w [pr.md](pr.md), sekcja *Decyzja*. Agent, który napisał kod, nie może tej zmiany zatwierdzić.
