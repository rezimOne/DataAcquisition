# Review: DA-004 — Wysylka obserwacji z workera do API

Status: changes-requested. Commit: 3d986d5. Baza: 751df55. Recenzent: sdlc-reviewer. Data: 2026-11-05.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review DA-004` lub `tools/Invoke-Review.ps1 -Id DA-004`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|
| R-01 | P2 | `Worker/Api/ApiObservationSink.cs:50` (oraz `:89`, test `Worker.Tests/Api/ApiObservationSinkTests.cs:129`) | Każda odpowiedź `400` jest traktowana jako „niepoprawna paczka” i paczka jest **pomijana**, także gdy treść nie jest `ProblemDetails` z API (`ReadInvalidFieldsAsync` zwraca wtedy „(response is not ProblemDetails JSON)”, a test `ReceiveAsync_BadRequestWithoutProblemDetails_LogsAndSkipsBatch` utrwala to zachowanie). Scenariusz: operator ustawia `DataAcquisition__Api__BaseUrl=http://api.example:443` za nginx/reverse proxy (albo trafia na inną usługę HTTP) — nginx odpowiada `400` HTML „The plain HTTP request was sent to HTTPS port” na każde żądanie. Worker loguje `Error` i przesuwa `nextSequence` dla **każdej** paczki: wszystkie dane z maszyny są trwale tracone, mimo że jeszcze leżą w buforze agenta (~22 min) i po poprawce konfiguracji dałyby się wysłać. Decyzja PO w `intent.md` dotyczy „400 — niepoprawna paczka” (błąd walidacji API z polami), a `spec.md` opisuje wiersz jako „`400` (`ValidationProblemDetails` lub `ProblemDetails`)”; `400` bez `ProblemDetails` nie jest odrzuceniem paczki przez API i bliżej mu do wiersza „inne 4xx → ponowienie”. Proponowana poprawka: pomijać paczkę tylko, gdy odpowiedź jest `ProblemDetails` z API (np. `Content-Type: application/problem+json` lub obecne `errors`/`status`), w pozostałych przypadkach rzucać `HttpRequestException` (ponowienie); dostosować test. Jeśli PO świadomie chce pomijać każde `400` — oznaczyć uwagę jako `odrzucona` z uzasadnieniem. | otwarta |

## Zastosowane skille

| Skill | Dla plików / obszaru | Uwagi |
|---|---|---|
| `dotnet-skills:csharp-coding-standards` | wszystkie zmienione `*.cs` | załadowany; jawne mapowanie metodą rozszerzającą (bez refleksji), rekordy, `switch` — zgodne |
| `dotnet-skills:csharp-nullable-reference-types` | wszystkie zmienione `*.cs` | załadowany; `ToDto` zwraca `ObservationDto?` choć nigdy nie zwraca `null` (wymuszone typem `IReadOnlyList<ObservationDto?>` kontraktu) — bez realnego skutku, nie zgłaszane |
| `dotnet-skills:csharp-api-design` | `Contracts/ObservationBatchRequest.cs`, `Contracts/ObservationResponses.cs` | załadowany; `Units`/`NativeCode` jako właściwości `init` — rozszerzenie zgodne źródłowo i binarnie (odstępstwo 1 uzasadnione) |
| `dotnet-skills:serialization` | kontrakt, `ApiObservationSink` | załadowany; System.Text.Json z ustawieniami web, enumy przez `StrictStringEnumConverter` z `Contracts`; brak nazw typów w payloadzie; pola opcjonalne — kompatybilność wire w obie strony (stare API pomija nieznane pola — opisane w `release.md`) |
| `dotnet-skills:microsoft-extensions-dependency-injection` | `Worker/Api/ApiServiceCollectionExtensions.cs`, `Worker/Program.cs`, `Worker/MTConnect/MTConnectServiceCollectionExtensions.cs` | załadowany; `AddDataAcquisitionApi()` zgodne ze wzorcem `Add*`; singletony bez zależności scoped |
| `dotnet-skills:microsoft-extensions-configuration` | `Worker/Api/ApiOptions.cs` | załadowany; `BindConfiguration` + `IValidateOptions` + `ValidateOnStart`, walidator zwraca `Fail` (nie rzuca) — zgodne |
| `dotnet-skills:csharp-concurrency-patterns` | `Worker/Api/ApiObservationSink.cs` | załadowany; async z `CancellationToken`, brak blokowania; timeout `HttpClient` (`TaskCanceledException`) trafia do ścieżki ponowienia synchronizatora (filtr `when cancellationToken.IsCancellationRequested`, istniejący test `MTConnectSynchronizerTests`) |
| `dotnet-skills:project-structure` | `DataAcquisition.slnx`, `Integration.Tests/*.csproj` | załadowany; płaska lista projektów w `.slnx` jak dotąd |
| `dotnet-skills:package-management` | `Integration.Tests/DataAcquisition.Integration.Tests.csproj`, `Worker/DataAcquisition.Worker.csproj` | załadowany; repozytorium nie używa CPM, wersje inline spójne z `Worker.Tests` (te same wersje) — brak uwag; ręczna edycja XML opisana w odstępstwie 2 |
| `dotnet-skills:slopwatch` | cała zmiana | załadowany; w diffie brak `Skip`, `#pragma`, pustych `catch`, `Task.Delay` w testach (E2E na `FakeTimeProvider`/`StepAsync`); `catch (JsonException)` w `ReadInvalidFieldsAsync` zwraca jawny opis — nie jest połykaniem błędu |

## Zgodność z planem

Wszystkie 20 zmienionych plików spoza `docs/` są objęte tabelą kroków w `plan.md` (kolumna „W planie?” w `pr.md` = tak dla każdego). Odstępstwa 1–4 są opisane w `plan.md` w sekcji *Odstępstwa*: (1) `init` zamiast parametrów konstruktora — uzasadnione kompatybilnością binarną; (2) `InternalsVisibleTo` dla `Integration.Tests` i ręczna edycja `Worker.csproj`; (3) ręczne spłaszczenie `.slnx`; (4) testy E2E po implementacji. Tryb: feature — reguła o niezmienionym teście reprodukującym nie dotyczy. Plan zawiera sekcję *Wydanie i odtworzenie* z nową wymaganą zmienną `DataAcquisition__Api__ApiKey`, kolejnością wdrożenia i rollbackiem.

## Pokrycie kryteriów akceptacji

| AC | Test / dowód w verification.md | Ocena recenzenta |
|---|---|---|
| AC-01 | Output build/test/format/slopwatch dla `df35353` z kodami 0 | pokryte |
| AC-02 | `ApiObservationSinkTests.ReceiveAsync_Accepted_…` (URL, metoda, `X-Api-Key`, wszystkie pola JSON, czas `+00:00`), `…BaseUrlWithPath_KeepsPath`, `ObservationBatchMappingTests`, E2E `WorkerToApiTests.Sink_SendsBatch_…`, `Api.Tests` dla nowych pól i paczki w formacie DA-001; test ręczny | pokryte |
| AC-03 | `…TransientStatus_…` (500/503/404/429), `…ConnectionFailure_Throws`, `…ApiDoesNotAnswer_TimesOutAfterRequestTimeout`, E2E `Synchronizer_ApiUnavailableForTwoRequests_…` (backoff 1 s, 2 s, komplet bez duplikatów); test ręczny z restartem API | pokryte |
| AC-04 | `…ValidationProblem_…`, `…BadRequestWithoutProblemDetails_…`, `…ApiKeyRejected_…` (401/403), asercje braku klucza i wartości w logach | pokryte testami; zachowanie dla `400` bez `ProblemDetails` — patrz R-01 |
| AC-05 | `WorkerTests.StartAsync_InvalidApiConfiguration_…` (6 przypadków), `Options_ApiWithKeyOnly_…`, `Services_DefaultRegistration_UsesApiSinkAndHttpAgent`; grep klucza w logach testu ręcznego (0 wystąpień) | pokryte |
| AC-06 | Test ręczny 150 s, restart API po 60 s na 20 s: 8887 = 8887, 0 luk, 0 duplikatów, ponowienia widoczne w logu | pokryte (dowód rzeczywisty, z liczbami i fragmentami logów) |

## Dowody weryfikacji

`verification.md` zawiera rzeczywisty output dla `df35353` (build 0 ostrzeżeń, testy 2/82/86, format i slopwatch kod 0) oraz przebiegi czerwone potwierdzające test-first (Api 2/86, Worker 20/82). Przejrzany HEAD `3d986d5` różni się od `df35353` wyłącznie plikami w `docs/` (`verification.md`, `release.md`, `pr.md`) — dowody dotyczą aktualnego kodu. Pominięcia są jawnie opisane i zasadne: 400/401/403 i timeout tylko w testach z fałszywym handlerem, mylący prefiks ostrzeżenia synchronizatora (plik poza planem — propozycja osobnego tematu), utrata danych przy restarcie API (store w pamięci, DA-001), nadpisywanie `appsettings.json` w katalogu wyjściowym `Integration.Tests` bez wpływu na testy.

## Historia iteracji

| Iteracja | Commit | Data | Wynik |
|---|---|---|---|
| 1 | 3d986d5 | 2026-11-05 | changes-requested: P1 0, P2 1, P3 0 |
