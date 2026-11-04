# Pull Request: DA-001 — API odbioru danych z maszyn

Status: draft. Gałąź: topic/DA-001-api-odbioru-danych. Baza: main. Autor: nog_programista2. Źródła: [intent](intent.md), [plan](plan.md), [verification](verification.md), [review](review.md).

> Cykl statusu: draft → approved. Status approved nadaje wyłącznie człowiek po przeczytaniu `review.md` (hook blokuje agenta). Push gałęzi przepuszcza dopiero `tools/Test-TopicReady.ps1` (hook git pre-push). Merge wykonuje człowiek na zdalnym repozytorium.

## Opis zmiany

W solucji nie było API, więc dane z maszyny CNC (agent MTConnect) nie miały dokąd trafić (intent.md). Zmiana dodaje usługę `DataAcquisition.Api` (ASP.NET Core Minimal API, .NET 10), która przyjmuje paczki obserwacji MTConnect od workera, odrzuca dane niepoprawne i niezautoryzowane, pomija duplikaty i udostępnia odczyt z filtrami. Kontrakty DTO są w osobnym projekcie `DataAcquisition.Contracts`, żeby worker w kolejnym temacie korzystał z tych samych typów. Przechowywanie jest w pamięci za interfejsem (decyzja PO — trwała baza w osobnym temacie). Worker nie jest zmieniany.

## Najważniejsze zmiany

- `Contracts/` — `ObservationBatchRequest`/`ObservationDto` (pola nullable, żeby braki zgłaszać jako błędy walidacji), odpowiedzi, enumy `ObservationCategory`/`ConditionLevel` serializowane jako stringi (atrybut na typie — niezależne od konfiguracji hosta), stałe `ApiContract` (nagłówek `X-Api-Key`, ścieżka, limit 1000).
- `Api/ApiKey/` — uwierzytelnianie jako `AuthenticationHandler` (nie endpoint filter), więc 401 zapada **przed** bindowaniem body; porównanie `CryptographicOperations.FixedTimeEquals`; klucz w `DataAcquisition:Api:ApiKey` z walidacją Options przy starcie (min. 32 znaki); 401 jako `ProblemDetails`.
- `Api/Observations/ObservationEndpoints.cs` — `POST /api/v1/observations` (walidacja całej paczki → zapis → `{accepted, duplicates}`), `GET /api/v1/observations?deviceId&from&to&limit` (`[from, to)`, domyślnie 100, max 1000, `truncated`).
- `Api/Observations/ObservationBatchValidator.cs` — ręczne reguły ze spec; klucze błędów to ścieżki pól (`observations[3].dataItemId`); reguły krzyżowe: `conditionLevel` tylko i zawsze dla `Condition`, unikalne `sequence` w paczce.
- `Api/Observations/InMemoryObservationStore.cs` — `ConcurrentDictionary.TryAdd` po kluczu (`deviceId`, `instanceId`, `sequence`), pierwszy zapis wygrywa; `Warning` (bez wartości) dla duplikatu o innej treści; sortowanie (`instanceId`, `sequence`).
- `Api/Program.cs` — `AddProblemDetails` + `UseExceptionHandler` + `UseStatusCodePages` (błędy bindowania JSON też jako `problem+json`), `/health` bez uwierzytelnienia; `launchSettings.json` na porcie 5100 (5000 zajmuje symulator).
- `Contracts/StrictStringEnumConverter.cs` (po review R-01) — enumy w JSON wyłącznie jako stringi; liczby (`7`) i liczbowe stringi (`"7"`) → `400`. Walidator dodatkowo sprawdza `Enum.IsDefined`.
- Zły / brakujący `Content-Type` → `415` `ProblemDetails` (zachowanie Minimal API; spec zmieniony po review R-02).
- `Api.Tests/` — 82 testy: integracyjne na `WebApplicationFactory` (środowisko `Testing`, losowy klucz per przebieg, `FakeLogger` do sprawdzania logów) + jednostkowe walidatora i store'a.
- `DataAcquisition.slnx` — 3 nowe projekty.

## Ryzyka i wpływ

- **Nowa wymagana konfiguracja**: bez `DataAcquisition__Api__ApiKey` (lub User Secrets w Development) API nie startuje — świadomie (AC-08).
- **Dane w pamięci**: giną po restarcie, pamięć rośnie bez limitu przy długiej pracy. Zaakceptowane w spec na etap deweloperski; interfejs `IObservationStore` izoluje przyszłą wymianę na bazę.
- **Odczyt O(n)**: `QueryAsync` filtruje i sortuje wszystkie obserwacje przy każdym GET — wystarczające dla dev, nie dla produkcji.
- **Kompatybilność**: tylko nowe projekty; worker i jego testy bez zmian (3/3 zielone). Brak migracji.
- **Bezpieczeństwo**: jeden współdzielony klucz, HTTP bez TLS lokalnie; role/OAuth/HTTPS poza zakresem.

## Jak testowano

- `dotnet build DataAcquisition.slnx` — 0 ostrzeżeń, 0 błędów; `dotnet test DataAcquisition.slnx` — Api.Tests 82/82, Worker.Tests 3/3; `dotnet format --verify-no-changes` — kod 0 (stan `d1d9f5d`; późniejsze commity zmieniają wyłącznie `docs/` — `git diff --stat d1d9f5d HEAD -- . ':!docs'` pusty). `dotnet publish` z `release.md` — kod 0.
- Test-first: na zaślepkach walidatora i store'a 61/73 czerwonych, po implementacji 73/73; po review 1 nowe testy R-01 czerwone 7/82 przed poprawką, 82/82 po.
- Ręcznie (`dotnet run`, curl na `:5100`): health 200, POST bez klucza 401, POST 200 `{1,0}`, powtórka `{0,1}`, GET zwraca dane w UTC, GET bez `deviceId` 400; start ze starą nazwą zmiennej `ApiKey__Value` → `OptionsValidationException`; klucz nie pojawia się w logu procesu (`grep -c` = 0). Dodatkowo potwierdzone działanie klucza z User Secrets.
- **Nie testowano**: obciążenia i długotrwałej pracy, integracji z workerem i symulatorem, HTTPS.

## Uwagi dla recenzenta

- **Odstępstwa od planu** (sekcja w `plan.md`): (1) `Api/appsettings*.json` utworzył człowiek — agent ma regułę odmowy odczytu, więc **nie zweryfikował ich treści**; proszę sprawdzić w diffie, że nie ma w nich klucza. (2) `timestamp` bez przesunięcia nie jest odrzucany (STJ przyjmuje czas lokalny serwera) mimo wymogu spec — do decyzji w temacie wysyłki z workera. (3) Commit `Api/` przed `Api.Tests/` (test-first w drzewie roboczym). (4) Klucz testowy losowy zamiast stałej (hook sekretów). (5) Nazwa klucza zmieniona na `DataAcquisition:Api:ApiKey` na prośbę człowieka — zmiana w zatwierdzonym `spec.md`. (7) `AuthenticationHandler` zamiast planowanego endpoint filtra (401 przed bindowaniem body). (8) Poprawki po review 1: `StrictStringEnumConverter` (R-01), spec `415` dla złego `Content-Type` (R-02). Treść `appsettings*.json` (zastrzeżenie z pkt 1) recenzent sprawdził w iteracji 1 — bez sekretów.
- **`.claude/sdlc.json`** (commit `183930d`): `testCommand` dla bramki `Test-TopicReady` ustawiony przez człowieka; zmiana konfiguracji procesu, nie kodu tematu — odstępstwo 6 w `plan.md`. W tym samym odstępstwie: blok kodu w sekcji *Weryfikacja* planu zamieniony na listę, bo potrójne backticki psuły rozpoznawanie ścieżek w `Test-PlanCovers` (błąd narzędzia — do poprawy przez człowieka w `.claude/hooks/SdlcCommon.ps1`).
- Warto spojrzeć na: `ApiKeyAuthenticationHandler` (kolejność względem bindowania, `provided.Count != 1` → brak uwierzytelnienia), `InMemoryObservationStore.AddAsync` (wyścig `TryAdd`/`TryGetValue` — wpływa tylko na log ostrzeżenia, nie na liczniki), sortowanie po `instanceId` (zakłada rosnące `instanceId` przy restartach agenta — w symulatorze to czas uniksowy).
- `ApiFactory` używa `ConfigureAppConfiguration` + `UseEnvironment("Testing")`, żeby lokalne User Secrets i zmienne środowiskowe dewelopera nie wpływały na testy startu (User Secrets potwierdzone — testy zielone przy ustawionym sekrecie; przesłonięcie zmiennej środowiskowej wynika z kolejności źródeł konfiguracji, nie zostało osobno przetestowane).

## Dane automatyczne

Sekcję generuje `tools/New-PullRequest.ps1` — nie edytuj ręcznie.

<!-- auto:start -->
Wygenerowano: 2026-11-04 11:40. Gałąź: `topic/DA-001-api-odbioru-danych`. Baza: `main` (merge-base `495a418`). **HEAD: `805917d`**.

Diff do review: `git diff 495a418 805917d` — pełna treść w `.claude/sdlc-tmp/DA-001-api-odbioru-danych.diff` (plik lokalny, nieśledzony przez git).

### Commity (14)

- `805917d` DA-001: poprawki po review 1 (R-02 spec 415, R-03 release.md, R-04 odstepstwo 7, verification d1d9f5d)
- `d1d9f5d` DA-001: R-01 odrzucanie liczbowych wartosci enumow; R-02 test 415
- `8344188` DA-001: pr.md - odswiezone dane automatyczne, odstepstwo 6
- `ef5d564` DA-001: plan.md - odstepstwo 6 (.claude/sdlc.json), lista komend zamiast bloku kodu
- `19ed643` DA-001: pr.md - opis PR dla recenzenta
- `507e7be` DA-001: verification.md - dowody dla d728000 (nazwa klucza, appsettings)
- `d728000` DA-001: klucz API jako DataAcquisition:Api:ApiKey; appsettings API
- `761c9e3` DA-001: verification.md - dowody AC-01..AC-08 dla 40df9fc
- `40df9fc` DA-001: dodanie projektow API, kontraktow i testow do solucji
- `0108e52` DA-001: testy DataAcquisition.Api.Tests (AC-02..AC-08, walidator, store)
- `873fe38` DA-001: projekt DataAcquisition.Api (Minimal API, klucz API, store w pamieci)
- `d94fdaf` DA-001: projekt DataAcquisition.Contracts (DTO obserwacji)
- `229e1f3` DA-001: intent, spec i plan API odbioru danych
- `183930d` SDLC: testCommand dla bramki Test-TopicReady

### Zmienione pliki

| Zmiana | Plik | W planie? |
|---|---|---|
| M | `.claude/sdlc.json` | tak |
| A | `Api.Tests/ApiFactory.cs` | tak |
| A | `Api.Tests/DataAcquisition.Api.Tests.csproj` | tak |
| A | `Api.Tests/HealthAndStartupTests.cs` | tak |
| A | `Api.Tests/InMemoryObservationStoreTests.cs` | tak |
| A | `Api.Tests/ObservationBatchValidatorTests.cs` | tak |
| A | `Api.Tests/ObservationsApiTests.cs` | tak |
| A | `Api.Tests/TestData.cs` | tak |
| A | `Api/ApiKey/ApiKeyAuthenticationHandler.cs` | tak |
| A | `Api/ApiKey/ApiKeyOptions.cs` | tak |
| A | `Api/ApiKey/ApiKeyServiceCollectionExtensions.cs` | tak |
| A | `Api/DataAcquisition.Api.csproj` | tak |
| A | `Api/Observations/IObservationStore.cs` | tak |
| A | `Api/Observations/InMemoryObservationStore.cs` | tak |
| A | `Api/Observations/ObservationBatchValidator.cs` | tak |
| A | `Api/Observations/ObservationEndpoints.cs` | tak |
| A | `Api/Program.cs` | tak |
| A | `Api/Properties/launchSettings.json` | tak |
| A | `Api/appsettings.Development.json` | tak |
| A | `Api/appsettings.json` | tak |
| A | `Contracts/ApiContract.cs` | tak |
| A | `Contracts/ConditionLevel.cs` | tak |
| A | `Contracts/DataAcquisition.Contracts.csproj` | tak |
| A | `Contracts/ObservationBatchRequest.cs` | tak |
| A | `Contracts/ObservationCategory.cs` | tak |
| A | `Contracts/ObservationResponses.cs` | tak |
| A | `Contracts/StrictStringEnumConverter.cs` | tak |
| M | `DataAcquisition.slnx` | tak |
| M | `docs/sdlc/01-plan/BACKLOG.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-001-api-odbioru-danych/intent.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-001-api-odbioru-danych/plan.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-001-api-odbioru-danych/release.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-001-api-odbioru-danych/spec.md` | dokumentacja |
| A | `docs/sdlc/tematy/DA-001-api-odbioru-danych/verification.md` | dokumentacja |

Statystyka: 34 files changed, 1978 insertions(+), 5 deletions(-)

### Kryteria akceptacji

| AC | Kryterium (intent.md) | Wynik (verification.md) |
|---|---|---|
| AC-01 | `dotnet build DataAcquisition.slnx` i `dotnet test DataAcquisition.slnx` kończą się kodem 0; solucja zawiera projekt API, kontraktów i testów API. | PASS |
| AC-02 | Poprawna paczka obserwacji wysłana z prawidłowym kluczem API jest przyjmowana (2xx), a obserwacje są następnie zwracane przez endpoint odczytu. | PASS |
| AC-03 | Żądanie zapisu bez klucza API lub z błędnym kluczem jest odrzucane (401) i nic nie zostaje zapisane. | PASS |
| AC-04 | Paczka niepoprawna (np. pusta, brak `deviceId`/`dataItemId`/`sequence`/`timestamp`, przekroczony limit rozmiaru) jest odrzucana (400) z opisem błędów w formacie `ProblemDetails` i nic z niej nie zostaje zapisane. | PASS |
| AC-05 | Ponowne wysłanie tych samych obserwacji (ten sam `deviceId` + `instanceId` + `sequence`) nie tworzy duplikatów; odpowiedź informuje, ile obserwacji przyjęto, a ile pominięto jako duplikaty. | PASS |
| AC-06 | Endpoint odczytu filtruje po `deviceId` i zakresie czasu, zwraca wyniki posortowane po `sequence` i respektuje limit liczby wyników. | PASS |
| AC-07 | Endpoint health check zwraca 200 bez uwierzytelnienia. | PASS |
| AC-08 | Klucz API nie jest zapisany w repozytorium ani w logach; aplikacja bez skonfigurowanego klucza nie startuje (walidacja Options przy starcie). | PASS (zastrzeżenie: treści `Api/appsettings*.json` agent nie może odczytać — patrz Pominięcia) |
<!-- auto:end -->

## Decyzja

- **Akceptacja do wypchnięcia**: <osoba, data — approved / changes-requested> — wpisuje człowiek po przeczytaniu `review.md`.
- **Zdalny PR / merge**: <link lub numer PR na zdalnym repozytorium, data merge — uzupełnia człowiek>.
