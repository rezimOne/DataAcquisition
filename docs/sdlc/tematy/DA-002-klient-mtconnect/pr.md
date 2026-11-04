# Pull Request: DA-002 — Klient MTConnect w workerze

Status: draft. Gałąź: topic/DA-002-klient-mtconnect. Baza: main. Autor: nog_programista2. Źródła: [intent](intent.md), [plan](plan.md), [verification](verification.md), [review](review.md).

> Cykl statusu: draft → approved. Status approved nadaje wyłącznie człowiek po przeczytaniu `review.md` (hook blokuje agenta). Push gałęzi przepuszcza dopiero `tools/Test-TopicReady.ps1` (hook git pre-push). Merge wykonuje człowiek na zdalnym repozytorium.

## Opis zmiany

Worker był szkieletem logującym co sekundę (intent.md). Zmiana dodaje klienta agenta MTConnect: worker pobiera model urządzenia (`/probe`), migawkę (`/current`) i w pętli nowe obserwacje (`/sample?from=nextSequence`), reaguje na restart agenta, wypadnięcie z bufora (`OUT_OF_RANGE`) i niedostępność agenta, a każdą paczkę przekazuje przez `IObservationSink`. W tym temacie odbiorca tylko loguje statystyki — wysyłka do API (DA-001) to DA-003, który podmieni wyłącznie implementację odbiorcy. API i `Contracts/` bez zmian.

## Najważniejsze zmiany

- `Worker/MTConnect/MTConnectParser.cs` — MTConnect 1.3 (Devices/Streams/Error) przez `XDocument`; obserwacje sortowane po `sequence` (w XML są grupowane po komponentach); kategoria z elementu `Samples`/`Events`/`Condition`, poziom alarmu z nazwy elementu, `nativeCode`; obcy namespace/zły XML/brak atrybutów → `MTConnectFormatException`; parsowanie niezależne od kultury, czas w UTC.
- `Worker/MTConnect/MTConnectSynchronizer.cs` — stany Niezsynchronizowany/Strumień; `StepAsync` (jeden cykl + obsługa błędów, zwraca opóźnienie) i `RunAsync` (pętla na `TimeProvider`); doganianie (`nextSequence ≤ lastSequence` → bez czekania); restart agenta wykrywany w synchronizacji po `/current` (także gdy objawia się jako `OUT_OF_RANGE`); po resynchronizacji `/sample` od razu i od danych wciąż w buforze agenta (`firstSequence`; przy pełnym buforze z zapasem `min(SampleCount, bufferSize/10)`), pierwszy start od `nextSequence` (po review R-01); `nextSequence` przesuwany dopiero po przyjęciu paczki przez odbiorcę (at-least-once); backoff 1 s → 30 s z resetem; `type`/`subType`/`units` z `/probe`.
- `Worker/MTConnect/MTConnectClient.cs` — named `HttpClient` z `IHttpClientFactory` per żądanie (singleton synchronizatora nie trzyma jednego klienta); 404 z `MTConnectError` → `MTConnectAgentErrorException`, inne błędy HTTP → `HttpRequestException`; bez logów każdego żądania (`RemoveAllLoggers`).
- `Worker/MTConnect/MTConnectOptions.cs` — `DataAcquisition:MTConnect:*` z domyślnymi wartościami w kodzie (symulator `http://localhost:5000`, 1 s, 1000, 10 s, 1 s → 30 s) i walidacją przy starcie (komunikat z pełnym kluczem).
- `Worker/Worker.cs`, `Worker/Program.cs` — `BackgroundService` uruchamia synchronizator; rejestracja `AddMTConnect()`.
- `Worker.Tests/` — fixtures XML z symulatora + przypadki ręczne; 54 testy (parser, klient z fałszywym handlerem, synchronizator z fałszywym agentem i `FakeTimeProvider`, host). Poprzednie 3 testy szkieletu zastąpione.

## Ryzyka i wpływ

- **Nowe zachowanie workera**: łączy się z agentem przy starcie; bez agenta loguje `Warning` co ≤ 30 s (nie kończy pracy). Domyślny adres to lokalny symulator — na serwerze trzeba ustawić `DataAcquisition__MTConnect__AgentUrl`.
- **At-least-once**: migawka `/current` przy każdej synchronizacji i ponowienie paczki po błędzie odbiorcy dają duplikaty — świadomie, bo API (DA-001) jest idempotentne.
- **Luka po `OUT_OF_RANGE`**: przy przestoju dłuższym niż bufor agenta (~22 min przy ~100 obs./s symulatora) przepada tylko to, co wypadło z bufora (plus zapas ≤ 10% bufora przy pełnym buforze); worker loguje dokładny zakres i od razu czyta resztę bufora. Po dogonieniu bufora worker nadrabia z prędkością ~1000 obs./żądanie.
- **Zakres formatu**: tylko MTConnect 1.3 (namespace sprawdzany jawnie); realny agent w innej wersji zostanie odrzucony z czytelnym `Warning`, nie sparsowany błędnie.
- **Brak zmian** w API, `Contracts/`, konfiguracji `appsettings`; brak migracji.

## Jak testowano

- `dotnet build` (0 ostrzeżeń), `dotnet test` (Worker.Tests 54/54, Api.Tests 82/82), `dotnet format --verify-no-changes` — kod 0 dla `6538cca`; `dotnet publish` z `release.md` — kod 0.
- Test-first: parser 15/18 czerwonych na zaślepce, synchronizator 18/36; po review: 4/53 czerwone przed R-01, 3/54 przed poprawką `6538cca`.
- **Test ręczny z symulatorem (AC-08)** wykrył błąd: po restarcie symulatora brak logu o restarcie i ponownego `/probe` (restart objawił się jako `OUT_OF_RANGE`). Test reprodukujący `3cb00ba` (czerwony) → poprawka `96142b5` → przebieg 2: 0 luk.
- **Po review (R-01)** test ręczny z `--buffer 1000` i `PollInterval` 30 s wykrył 2 błędy pierwszej poprawki (czekanie po resync, zapas = cały bufor) → `6538cca`. Przebieg 5: po każdym `OUT_OF_RANGE` od razu 900 obserwacji z bufora, dokładny zakres luki, bez pętli. Przebieg 6 (restart): nowa instancja od sekwencji 1, 126 paczek, **0 luk**.
- **Nie testowano**: realnego agenta (cppagent), `OUT_OF_RANGE` na pełnym buforze 131072 na żywo (tylko test jednostkowy), timeoutu i 5xx na żywo (tylko fałszywy handler/agent).

## Uwagi dla recenzenta

- **Odstępstwa od planu** (`plan.md`): (1) named client zamiast typed client + `RemoveAllLoggers`; (2) `sample-empty.xml` ręczny (symulator za szybki, by złapać pusty `/sample`); (3) test-first tylko dla parsera i synchronizatora; (4) tempo symulatora ~100 obs./s (bufor ~22 min, nie ~17); (5) doprecyzowanie spec: wykrywanie restartu po `/current` niezależnie od ścieżki; (6) po review R-01: wznowienie z bufora agenta, `BufferSize` w `StreamsHeader`, zmienione oczekiwania 3 testów (w tym `from` w teście z `3cb00ba`); (7) 2 błędy pierwszej poprawki R-01 znalezione w teście ręcznym. Spec zaktualizowany (pkt 2.3–2.4, 3.3), `release.md` wypełniony (R-02).
- Warto spojrzeć na: `MTConnectSynchronizer.SynchronizeAsync` (kolejność `/probe` ↔ `/current` przy restarcie wykrytym przez `OUT_OF_RANGE`: `/current` → `/probe`, a przy zmianie `instanceId` w `/sample`: `/probe` → `/current`), `StepAsync` (łapie wszystko poza anulowaniem przy zatrzymaniu — świadomie, by `BackgroundService` nie zatrzymał hosta), wzbogacanie typów z `/probe` (`Enrich`).
- `NativeCode` (kod alarmu) jest w modelu workera, ale nie ma pola w kontrakcie API — decyzja w DA-003.
- Commity: `2ffd2f2` (fixtures, model, parser), `c5cbe4a` (klient, synchronizator, rejestracja), `3cb00ba` (czerwony test regresji), `96142b5` (poprawka), `bf41614` (R-01–R-03), `6538cca` (poprawka R-01 po teście ręcznym).

## Dane automatyczne

Sekcję generuje `tools/New-PullRequest.ps1` — nie edytuj ręcznie.

<!-- auto:start -->
Wygenerowano: 2026-11-04 14:39. Gałąź: `topic/DA-002-klient-mtconnect`. Baza: `main` (merge-base `417e369`). **HEAD: `d11c13f`**.

Diff do review: `git diff 417e369 d11c13f` — pełna treść w `.claude/sdlc-tmp/DA-002-klient-mtconnect.diff` (plik lokalny, nieśledzony przez git).

### Commity (12)

- `d11c13f` DA-002: pr.md - dane automatyczne
- `10049e8` DA-002: verification.md dla 6538cca, pr.md po review 1
- `6538cca` DA-002: /sample od razu po synchronizacji; zapas wznowienia ograniczony do 10% bufora
- `bf41614` DA-002: R-01 wznowienie z bufora agenta po resync; R-02 release.md; R-03 dokladne asercje zakresu
- `68c98af` DA-002: pr.md - dane automatyczne
- `107bc20` DA-002: pr.md - opis PR dla recenzenta
- `62a493c` DA-002: verification.md (AC-01..AC-08 dla 96142b5), odstepstwo 5 w plan.md
- `96142b5` DA-002: wykrywanie restartu agenta w synchronizacji (takze po OUT_OF_RANGE)
- `3cb00ba` DA-002: test reprodukujacy - restart agenta wykryty przez OUT_OF_RANGE nie robi probe (czerwony)
- `c5cbe4a` DA-002: klient HTTP, synchronizator, odbiorca logujacy, rejestracja w workerze
- `2ffd2f2` DA-002: fixtures MTConnect, model i parser z testami
- `6195175` DA-002: intent (ready), spec (approved), plan (draft); DA-001 verified

### Zmienione pliki

| Zmiana | Plik | W planie? |
|---|---|---|
| M | `Worker.Tests/DataAcquisition.Worker.Tests.csproj` | tak |
| A | `Worker.Tests/Fixtures/current.xml` | tak |
| A | `Worker.Tests/Fixtures/error-out-of-range.xml` | tak |
| A | `Worker.Tests/Fixtures/malformed.xml` | tak |
| A | `Worker.Tests/Fixtures/probe.xml` | tak |
| A | `Worker.Tests/Fixtures/sample-conditions.xml` | tak |
| A | `Worker.Tests/Fixtures/sample-empty.xml` | tak |
| A | `Worker.Tests/Fixtures/sample.xml` | tak |
| A | `Worker.Tests/Fixtures/streams-foreign-namespace.xml` | tak |
| A | `Worker.Tests/MTConnect/Fixtures.cs` | tak |
| A | `Worker.Tests/MTConnect/MTConnectClientTests.cs` | tak |
| A | `Worker.Tests/MTConnect/MTConnectParserTests.cs` | tak |
| A | `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs` | tak |
| A | `Worker.Tests/MTConnect/TestDoubles.cs` | tak |
| M | `Worker.Tests/WorkerTests.cs` | tak |
| M | `Worker/DataAcquisition.Worker.csproj` | tak |
| A | `Worker/MTConnect/MTConnectClient.cs` | tak |
| A | `Worker/MTConnect/MTConnectModel.cs` | tak |
| A | `Worker/MTConnect/MTConnectOptions.cs` | tak |
| A | `Worker/MTConnect/MTConnectParser.cs` | tak |
| A | `Worker/MTConnect/MTConnectServiceCollectionExtensions.cs` | tak |
| A | `Worker/MTConnect/MTConnectSynchronizer.cs` | tak |
| A | `Worker/MTConnect/ObservationSinks.cs` | tak |
| M | `Worker/Program.cs` | tak |
| M | `Worker/Worker.cs` | tak |
| M | `docs/sdlc/01-plan/BACKLOG.md` | dokumentacja |
| M | `docs/sdlc/tematy/DA-001-api-odbioru-danych/intent.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-002-klient-mtconnect/intent.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-002-klient-mtconnect/plan.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-002-klient-mtconnect/release.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-002-klient-mtconnect/spec.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-002-klient-mtconnect/verification.md` | dokumentacja |

Statystyka: 32 files changed, 2263 insertions(+), 76 deletions(-)

### Kryteria akceptacji

| AC | Kryterium (intent.md) | Wynik (verification.md) |
|---|---|---|
| AC-01 | `dotnet build DataAcquisition.slnx`, `dotnet test DataAcquisition.slnx` i `dotnet format DataAcquisition.slnx --verify-no-changes` kończą się kodem 0. | PASS |
| AC-02 | Parser zamienia odpowiedzi `/current` i `/sample` (fixtures XML w formacie agenta, w tym CONDITION `Normal`/`Fault`, wartość `UNAVAILABLE`, puste `/sample`) na obserwacje z poprawnymi polami oraz nagłówek (`instanceId`, `nextSequence`, `firstSequence`, `lastSequence`); `type`/`subType` pochodzą z `/probe`; czas w UTC. | PASS |
| AC-03 | Po starcie worker wykonuje `/probe` → `/current` → `/sample?from=<nextSequence>` i w kolejnych cyklach używa `nextSequence` z poprzedniej odpowiedzi; przy ciągłej pracy każda obserwacja trafia do odbiorcy dokładnie raz i w kolejności `sequence` (brak luk i duplikatów w ramach jednej instancji agenta). | PASS |
| AC-04 | Zmiana `Header/@instanceId` powoduje resynchronizację przez `/current` i log `Information`; dane nowej instancji są pobierane dalej bez restartu workera. | PASS (po poprawkach `96142b5`, `bf41614`) |
| AC-05 | Odpowiedź `OUT_OF_RANGE` powoduje resynchronizację przez `/current` i log `Warning` z wielkością utraconego zakresu sekwencji; worker działa dalej. | PASS (po poprawkach `bf41614`, `6538cca`) |
| AC-06 | Niedostępny agent, timeout, HTTP 5xx lub niepoprawny XML → log `Warning`, ponowienie z rosnącym opóźnieniem (z górnym limitem); po powrocie agenta pobieranie wznawia się bez restartu workera; host nie kończy pracy. | PASS |
| AC-07 | Konfiguracja (adres agenta, interwał, `count`, timeout) ładowana przez Options z walidacją przy starcie — błędna wartość zatrzymuje start z komunikatem wskazującym klucz; zatrzymanie hosta (Ctrl+C / `StopAsync`) kończy pętlę bez wyjątku. | PASS |
| AC-08 | Test ręczny z symulatorem (`--seed 42`, min. 2 minuty): ciągłość sekwencji w logach odbiorcy, w tym po restarcie symulatora w trakcie pracy workera (zmiana `instanceId`). | PASS (przebieg 2 na `96142b5`; przebieg 6 na `6538cca`) |
<!-- auto:end -->

## Decyzja

- **Akceptacja do wypchnięcia**: <osoba, data — approved / changes-requested> — wpisuje człowiek po przeczytaniu `review.md`.
- **Zdalny PR / merge**: <link lub numer PR na zdalnym repozytorium, data merge — uzupełnia człowiek>.
