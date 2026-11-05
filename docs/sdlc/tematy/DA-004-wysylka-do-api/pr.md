# Pull Request: DA-004 — Wysylka obserwacji z workera do API

Status: draft. Gałąź: topic/DA-004-wysylka-do-api. Gałąź bazowa: main. Autor: nog_programista2. Źródła: [intent](intent.md), [plan](plan.md), [verification](verification.md), [review](review.md).

> Cykl statusu: draft → approved. Status approved nadaje wyłącznie człowiek po przeczytaniu `review.md` (hook blokuje agenta) — w tym samym commicie ustawia `Status: done` w `plan.md`. Push gałęzi przepuszcza dopiero `tools/Test-TopicReady.ps1` (hook git pre-push). Merge wykonuje człowiek na zdalnym repozytorium.

## Opis zmiany

Domyka przepływ symulator → worker → API: worker zamiast logować paczki (DA-002) wysyła je do `POST /api/v1/observations` z kluczem `X-Api-Key`. Odpowiedzi API są klasyfikowane zgodnie z decyzją PO: `200` — log wyniku, `400` z ProblemDetails API — log `Error` z nazwami pól i pominięcie paczki (inne `400`, np. z proxy, są ponawiane — review R-01), `401`/`403` i pozostałe błędy — ponowienie przez synchronizator (at-least-once; API pomija duplikaty). Kontrakt API rozszerzony kompatybilnie o opcjonalne `units` i `nativeCode` (kod alarmu).

## Najważniejsze zmiany

- `Contracts/` — `ObservationDto`, `StoredObservationDto`: `Units`, `NativeCode` jako właściwości `init` (nie parametry konstruktora — kompatybilność źródłowa i binarna wg `csharp-api-design`).
- `Api/Observations/` — walidacja długości nowych pól (≤ 64), zapis i odczyt.
- `Worker/Api/ApiOptions.cs` — `DataAcquisition:Api:BaseUrl|ApiKey|RequestTimeout`, walidacja przy starcie (komunikat z kluczem, bez wartości).
- `Worker/Api/ObservationBatchMapping.cs` — jawne mapowanie modelu workera na kontrakt (enumy przez `switch`).
- `Worker/Api/ApiObservationSink.cs` — named `HttpClient` z `IHttpClientFactory`, ścieżka względna do `BaseAddress` (zachowuje ścieżkę bazową), `RemoveAllLoggers`, klasyfikacja odpowiedzi, logi bez wartości obserwacji i klucza.
- `Worker/Api/ApiServiceCollectionExtensions.cs` + `Worker/Program.cs` — `AddDataAcquisitionApi()`; `LoggingObservationSink` usunięty.
- Testy: `Api.Tests` +4, `Worker.Tests` +29 (odbiorca, mapowanie, konfiguracja hosta), nowy projekt `Integration.Tests` (worker → prawdziwe API w `TestServer`, także z przerwą API).

## Ryzyka i wpływ

- **Nowa wymagana konfiguracja workera**: bez `DataAcquisition__Api__ApiKey` (lokalnie: User Secrets **projektu Worker** — osobne niż API) worker nie startuje. Instrukcja w `release.md`.
- **Kolejność wdrożenia**: API przed workerem (stare API zignoruje `units`/`nativeCode`).
- **400 z ProblemDetails API = utrata paczki** — świadoma decyzja PO; log `Error` z nazwami pól. `400` bez ProblemDetails (proxy, zły port/schemat) jest ponawiane, żeby błąd konfiguracji nie gubił danych (R-01).
- **Dane w API w pamięci** — restart API je kasuje (bez zmian względem DA-001).
- Gałąź zawiera też commit człowieka `751df55` (pusty `.slopwatch/baseline.json` + `config.json.example`) — trafił tu, bo temat wyszedł od lokalnego `main`; na zdalnym PR będzie widoczny w diffie.

## Jak testowano

- `dotnet build` (0 ostrzeżeń), `dotnet test --nologo` (Api 86/86, Worker 84/84, Integration 2/2), `dotnet format --verify-no-changes`, `slopwatch --fail-on warning` (0 znalezisk) — kod 0 dla `3f1e456`.
- Test-first: API 2/86 czerwone przed zmianą API; worker 20/82 czerwone na zaślepkach odbiorcy i mapowania; po review R-01 2/84 czerwone przed poprawką.
- Test ręczny (AC-06, na `df35353` — poprawka R-01 dotyczy tylko ścieżki `400`, która w nim nie wystąpiła): symulator + API + worker 150 s, API zabite po 60 s na 20 s. Po restarcie API: worker `accepted` 8887 = 8887 obserwacji w API, zakres 6168–15054, **0 luk, 0 duplikatów**; paczka z przerwy ponowiona, potem doganianie paczkami po 1000. Klucz nie występuje w logach API ani workera.
- **Nie testowano na żywo**: 400, 401/403, timeout API (tylko testy z fałszywym handlerem).

## Uwagi dla recenzenta

- **Odstępstwa** (`plan.md`): (1) `init` zamiast parametrów konstruktora; (2) `InternalsVisibleTo` dla `Integration.Tests` w `Worker.csproj`, referencja dodana ręcznie; (3) `.slnx` poprawiony ręcznie po `dotnet sln add` (dodał foldery); (4) testy E2E po implementacji, konflikt `Program`/`appsettings` nie wystąpił (fabryka typowana `ApiKeyOptions`); (5) po review R-01: pomijanie tylko `400` z `application/problem+json`.
- **Wymagane skille**: `csharp-coding-standards`, `csharp-nullable-reference-types`, `csharp-api-design`, `serialization`, `microsoft-extensions-dependency-injection`, `microsoft-extensions-configuration`, `csharp-concurrency-patterns`, `project-structure`, `package-management`, `slopwatch`.
- **Obserwacja spoza zakresu**: ostrzeżenie synchronizatora ma prefiks „MTConnect agent … cycle failed” także przy awarii API — propozycja poprawki w osobnym temacie (`MTConnectSynchronizer.cs` nie jest w planie).
- Warto spojrzeć na: `ApiObservationSink.ReceiveAsync` (kolejność: sukces → 400 z ProblemDetails API → 401/403 → reszta, w tym 400 spoza API), `ReadInvalidFieldsAsync` (tylko nazwy pól), `WorkerToApiTests.Synchronizer_ApiUnavailable…` (opóźnienia 1, 2, 0, 1 s — po synchronizacji `/sample` od razu).

## Dane automatyczne

Sekcję generuje `tools/New-PullRequest.ps1` — nie edytuj ręcznie.

<!-- auto:start -->
Wygenerowano: 2026-11-05 08:49. Gałąź: `topic/DA-004-wysylka-do-api`. Baza: `main` (merge-base `751df55`). **HEAD: `6343c3d`**.

Diff do review: `git diff 751df55 6343c3d` — pełna treść w `.claude/sdlc-tmp/DA-004-wysylka-do-api.diff` (plik lokalny, nieśledzony przez git).

### Commity (11)

- `6343c3d` DA-004: pr.md - dane automatyczne
- `41c97cf` DA-004: verification.md i pr.md po R-01; review iteracja 1
- `3f1e456` DA-004: R-01 pomijanie paczki tylko przy 400 z ProblemDetails API
- `3d986d5` DA-004: pr.md - opis PR dla recenzenta
- `58205e7` DA-004: verification.md (AC-01..AC-06 dla df35353), release.md
- `df35353` DA-004: testy end-to-end Integration.Tests (worker -> API), projekt w solucji
- `3818320` DA-004: odbiorca ApiObservationSink w workerze (wysylka do API)
- `9aed68d` DA-004: opcjonalne Units i NativeCode w kontrakcie i API
- `9401447` DA-004: plan approved (akceptacja czlowieka)
- `830e13c` DA-004: spec approved, plan (draft)
- `3741770` DA-004: intent (ready), spec (draft); DA-003 verified

### Zmienione pliki

| Zmiana | Plik | W planie? |
|---|---|---|
| M | `Api.Tests/ObservationBatchValidatorTests.cs` | tak |
| M | `Api.Tests/ObservationsApiTests.cs` | tak |
| M | `Api/Observations/ObservationBatchValidator.cs` | tak |
| M | `Api/Observations/ObservationEndpoints.cs` | tak |
| M | `Contracts/ObservationBatchRequest.cs` | tak |
| M | `Contracts/ObservationResponses.cs` | tak |
| M | `DataAcquisition.slnx` | tak |
| A | `Integration.Tests/DataAcquisition.Integration.Tests.csproj` | tak |
| A | `Integration.Tests/WorkerToApiTests.cs` | tak |
| A | `Worker.Tests/Api/ApiObservationSinkTests.cs` | tak |
| A | `Worker.Tests/Api/ObservationBatchMappingTests.cs` | tak |
| M | `Worker.Tests/WorkerTests.cs` | tak |
| A | `Worker/Api/ApiObservationSink.cs` | tak |
| A | `Worker/Api/ApiOptions.cs` | tak |
| A | `Worker/Api/ApiServiceCollectionExtensions.cs` | tak |
| A | `Worker/Api/ObservationBatchMapping.cs` | tak |
| M | `Worker/DataAcquisition.Worker.csproj` | tak |
| M | `Worker/MTConnect/MTConnectServiceCollectionExtensions.cs` | tak |
| M | `Worker/MTConnect/ObservationSinks.cs` | tak |
| M | `Worker/Program.cs` | tak |
| M | `docs/sdlc/01-plan/BACKLOG.md` | dokumentacja |
| M | `docs/sdlc/tematy/DA-003-porzadki-slopwatch/intent.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-004-wysylka-do-api/intent.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-004-wysylka-do-api/plan.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-004-wysylka-do-api/release.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-004-wysylka-do-api/spec.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-004-wysylka-do-api/verification.md` | dokumentacja |

Statystyka: 27 files changed, 1309 insertions(+), 23 deletions(-)

### Kryteria akceptacji

| AC | Kryterium (intent.md) | Wynik (verification.md) |
|---|---|---|
| AC-01 | `dotnet build`, `dotnet test DataAcquisition.slnx --nologo`, `dotnet format --verify-no-changes` i `dotnet tool run slopwatch analyze --fail-on warning` kończą się kodem 0. | PASS |
| AC-02 | Paczka z workera jest wysyłana do API z nagłówkiem `X-Api-Key`; pola obserwacji (urządzenie, `instanceId`, `dataItemId`, `sequence`, czas UTC, kategoria, `type`, `subType`, wartość, poziom CONDITION) są zmapowane 1:1 na kontrakt; po wysyłce obserwacje są zwracane przez `GET /api/v1/observations`. | PASS |
| AC-03 | API niedostępne, timeout lub HTTP 5xx → odbiorca zgłasza niepowodzenie, synchronizator ponawia tę samą paczkę z rosnącym opóźnieniem, po powrocie API paczka jest dostarczona; brak utraty i duplikatów w API. | PASS |
| AC-04 | Błędy trwałe (np. 400 — niepoprawna paczka, 401 — zły klucz) są obsłużone zgodnie z decyzją PO (niżej), z logiem na poziomie `Error`, bez wartości obserwacji i bez klucza. | PASS (po poprawce R-01) |
| AC-05 | Konfiguracja `DataAcquisition:Api:*` ładowana przez Options z walidacją przy starcie (brak/za krótki klucz, zły adres → start przerwany z pełnym kluczem w komunikacie); klucz nie pojawia się w logach. | PASS |
| AC-06 | Test ręczny ≥ 2 min (symulator + API + worker): liczba obserwacji zwróconych przez API dla urządzenia = liczba unikalnych obserwacji przekazanych przez worker; zatrzymanie i ponowne uruchomienie API w trakcie nie powoduje luki. | PASS |
<!-- auto:end -->

## Decyzja

- **Akceptacja do wypchnięcia**: <osoba, data — approved / changes-requested> — wpisuje człowiek po przeczytaniu `review.md`; razem z nią `plan.md` → `Status: done` (plan przestaje autoryzować edycję plików), a temat w `intent.md` / `BACKLOG.md` → `verified`.
- **Zdalny PR / merge**: <link lub numer PR na zdalnym repozytorium, data merge — uzupełnia człowiek>.
