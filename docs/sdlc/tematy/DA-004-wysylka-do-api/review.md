# Review: DA-004 — Wysylka obserwacji z workera do API

Status: no-blocking-findings. Commit: 6343c3d. Baza: 751df55. Recenzent: sdlc-reviewer. Data: 2026-11-05.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review DA-004` lub `tools/Invoke-Review.ps1 -Id DA-004`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|
| R-01 | P2 | `Worker/Api/ApiObservationSink.cs:50` (oraz `:89`, test `Worker.Tests/Api/ApiObservationSinkTests.cs:129`) | Każda odpowiedź `400` jest traktowana jako „niepoprawna paczka” i paczka jest **pomijana**, także gdy treść nie jest `ProblemDetails` z API (`ReadInvalidFieldsAsync` zwraca wtedy „(response is not ProblemDetails JSON)”, a test `ReceiveAsync_BadRequestWithoutProblemDetails_LogsAndSkipsBatch` utrwala to zachowanie). Scenariusz: operator ustawia `DataAcquisition__Api__BaseUrl=http://api.example:443` za nginx/reverse proxy (albo trafia na inną usługę HTTP) — nginx odpowiada `400` HTML „The plain HTTP request was sent to HTTPS port” na każde żądanie. Worker loguje `Error` i przesuwa `nextSequence` dla **każdej** paczki: wszystkie dane z maszyny są trwale tracone, mimo że jeszcze leżą w buforze agenta (~22 min) i po poprawce konfiguracji dałyby się wysłać. Decyzja PO w `intent.md` dotyczy „400 — niepoprawna paczka” (błąd walidacji API z polami), a `spec.md` opisuje wiersz jako „`400` (`ValidationProblemDetails` lub `ProblemDetails`)”; `400` bez `ProblemDetails` nie jest odrzuceniem paczki przez API i bliżej mu do wiersza „inne 4xx → ponowienie”. Proponowana poprawka: pomijać paczkę tylko, gdy odpowiedź jest `ProblemDetails` z API (np. `Content-Type: application/problem+json` lub obecne `errors`/`status`), w pozostałych przypadkach rzucać `HttpRequestException` (ponowienie); dostosować test. Jeśli PO świadomie chce pomijać każde `400` — oznaczyć uwagę jako `odrzucona` z uzasadnieniem. **Iteracja 2 (`3f1e456`)**: `case HttpStatusCode.BadRequest when IsApiProblem(response)` (`ApiObservationSink.cs:50`, `:75-76`) — pomijanie tylko przy `application/problem+json`; inne `400` trafiają do `default` i rzucają `HttpRequestException` (ponowienie). Testy: `ReceiveAsync_ApiProblemWithoutFieldErrors_LogsAndSkipsBatch`, `ReceiveAsync_BadRequestNotFromApi_ThrowsSoBatchIsRetried` (HTML „plain HTTP…” i `application/json`; asercja braku logu `Error`), czerwone przed poprawką (2/84 w `verification.md`). Że API odpowiada na każde własne `400` typem `application/problem+json`, potwierdza kod (`TypedResults.ValidationProblem` w `ObservationEndpoints.cs:25`, `:82`) i istniejące `AssertProblemAsync` w `Api.Tests`. `spec.md` (tabela odpowiedzi) i `plan.md` (odstępstwo 5) zaktualizowane. | naprawiona |

## Zastosowane skille

| Skill | Dla plików / obszaru | Uwagi |
|---|---|---|
| `dotnet-skills:csharp-coding-standards` | wszystkie zmienione `*.cs` (iteracja 2: `Worker/Api/ApiObservationSink.cs`, `Worker.Tests/Api/ApiObservationSinkTests.cs`) | załadowany; dopasowanie wzorca `case … when` w `switch`, `CancellationToken` przekazywany do odczytu treści — zgodne |
| `dotnet-skills:csharp-nullable-reference-types` | wszystkie zmienione `*.cs` | załadowany; `ContentType?.MediaType` obsługuje brak nagłówka (`null` → nie-ProblemDetails → ponowienie) — zgodne |
| `dotnet-skills:serialization` | kontrakt, `ApiObservationSink` | załadowany; rozpoznanie odpowiedzi po typie mediów zamiast heurystyki treści — zgodne; kontrakt bez zmian od iteracji 1 |
| `dotnet-skills:slopwatch` | cała zmiana | załadowany; nowe testy bez `Skip`, `Task.Delay`, `#pragma`; `catch (JsonException)` zwraca jawny opis; slopwatch 0 znalezisk dla `3f1e456` |
| `dotnet-skills:csharp-api-design`, `dotnet-skills:microsoft-extensions-dependency-injection`, `dotnet-skills:microsoft-extensions-configuration`, `dotnet-skills:csharp-concurrency-patterns`, `dotnet-skills:project-structure`, `dotnet-skills:package-management` | `Contracts/*`, `Worker/Api/ApiOptions.cs`, `ApiServiceCollectionExtensions.cs`, `Program.cs`, `*.csproj`, `.slnx` | zastosowane w iteracji 1 (wyniki bez uwag — patrz historia); w iteracji 2 nie ładowane ponownie, bo pliki z tych obszarów nie zmieniły się od `3d986d5` (poprawka R-01 dotyczy wyłącznie `ApiObservationSink.cs` i jego testów) |

## Zgodność z planem

Wszystkie 20 zmienionych plików spoza `docs/` są objęte tabelą kroków w `plan.md` (kolumna „W planie?” w `pr.md` = tak dla każdego). Odstępstwa 1–5 opisane w `plan.md` → *Odstępstwa*; nowe odstępstwo 5 (po R-01) opisuje zmianę zachowania i zastąpienie testu `…BadRequestWithoutProblemDetails_LogsAndSkipsBatch` dwoma nowymi testami — w trybie `feature` zmiana testu jest dopuszczalna i uzasadniona decyzją PO; `spec.md` zaktualizowany spójnie. Sekcja *Wydanie i odtworzenie* bez zmian i kompletna (zmienna `DataAcquisition__Api__ApiKey`, kolejność wdrożenia API → worker, rollback).

## Pokrycie kryteriów akceptacji

| AC | Test / dowód w verification.md | Ocena recenzenta |
|---|---|---|
| AC-01 | Build/test/format/slopwatch z kodami 0 dla `3f1e456` (Integration 2/2, Worker 84/84, Api 86/86) | pokryte |
| AC-02 | `ApiObservationSinkTests.ReceiveAsync_Accepted_…`, `…BaseUrlWithPath_KeepsPath`, `ObservationBatchMappingTests`, E2E `WorkerToApiTests.Sink_SendsBatch_…`, `Api.Tests` dla nowych pól; test ręczny | pokryte |
| AC-03 | `…TransientStatus_…` (500/503/404/429), `…ConnectionFailure_Throws`, `…ApiDoesNotAnswer_TimesOutAfterRequestTimeout`, E2E `Synchronizer_ApiUnavailableForTwoRequests_…`; test ręczny z restartem API | pokryte |
| AC-04 | `…ValidationProblem_…`, `…ApiProblemWithoutFieldErrors_…`, `…BadRequestNotFromApi_…` (2), `…ApiKeyRejected_…` (401/403), asercje braku klucza i wartości w logach | pokryte (po R-01 zgodne ze `spec.md`) |
| AC-05 | `WorkerTests.StartAsync_InvalidApiConfiguration_…` (6), `Options_ApiWithKeyOnly_…`, `Services_DefaultRegistration_…`; grep klucza w logach testu ręcznego | pokryte |
| AC-06 | Test ręczny 150 s na `df35353`, restart API: 8887 = 8887, 0 luk, 0 duplikatów | pokryte; niepowtórzenie po R-01 uzasadnione (zmiana dotyczy tylko ścieżki `400`, która w teście ręcznym nie występuje) |

## Dowody weryfikacji

`verification.md` (Commit: `3f1e456`) zawiera rzeczywisty output build/test/format/slopwatch z kodami 0 dla `3f1e456` oraz przebieg czerwony nowych testów R-01 (2/84) na kodzie z `3d986d5`. Przejrzany HEAD `6343c3d` różni się od `3f1e456` wg listy commitów w `pr.md` wyłącznie dokumentacją (`41c97cf`, `6343c3d` — `verification.md`, `pr.md`, `review.md`) — dowody dotyczą aktualnego kodu. Ograniczenie review: bez powłoki recenzent nie zweryfikował samodzielnie zawartości tych dwóch commitów poza diffem względem bazy. Pominięcia (400/401/403 i timeout tylko z fałszywym handlerem, mylący prefiks ostrzeżenia synchronizatora poza planem, utrata danych przy restarcie API, nadpisany `appsettings.json` w `Integration.Tests`) są jawnie opisane i zasadne. Opis w `pr.md` jest aktualny po R-01 (liczby testów, commit dowodów, odstępstwo 5).

## Historia iteracji

| Iteracja | Commit | Data | Wynik |
|---|---|---|---|
| 1 | 3d986d5 | 2026-11-05 | changes-requested: P1 0, P2 1, P3 0 |
| 2 | 6343c3d | 2026-11-05 | no-blocking-findings: R-01 naprawiona; otwarte P1 0, P2 0, P3 0 |
