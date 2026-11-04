# Spec: DA-004 — Wysylka obserwacji z workera do API

Status: draft. Odpowiedzialny: nog_programista2. Data: 2026-11-04. Źródło: [intent](intent.md).

## Zachowanie

### Wysyłka paczki — `ApiObservationSink` (implementacja `IObservationSink` w workerze)

Dla każdej paczki `ObservationBatch` (jedno urządzenie, jedna instancja agenta, 1–1000 obserwacji, posortowane po `sequence`) odbiorca wysyła `POST {BaseUrl}/api/v1/observations` z nagłówkiem `X-Api-Key` i treścią `ObservationBatchRequest` (JSON, kontrakt z `Contracts/`). Wynik:

| Odpowiedź API | Zachowanie odbiorcy | Skutek w synchronizatorze (DA-002) |
|---|---|---|
| `200` z `{accepted, duplicates}` | Log `Information`: urządzenie, `instanceId`, źródło, zakres `sequence`, `accepted`, `duplicates`; zwraca się normalnie | Paczka dostarczona, `nextSequence` przesunięty |
| `400` (`ValidationProblemDetails` lub `ProblemDetails`) | Log `Error`: urządzenie, zakres `sequence`, **nazwy pól** z błędami (np. `observations[3].value`) — bez wartości obserwacji; zwraca się normalnie (**paczka pominięta**, decyzja PO) | Paczka traktowana jako dostarczona — strumień nie blokuje się |
| `401` / `403` | Log `Error` „API rejected the API key” (bez wartości klucza); rzuca wyjątek | Błąd przejściowy: `Warning` + ponowienie tej samej paczki z backoffem 1 s → 30 s (decyzja PO) |
| `5xx`, inne `4xx` (np. `404` zły adres, `415`, `429`), brak połączenia, `RequestTimeout` | Rzuca wyjątek (bez dodatkowego logu — loguje synchronizator) | Błąd przejściowy: ponowienie tej samej paczki |
| Anulowanie przy zatrzymaniu hosta | Przepuszcza `OperationCanceledException` | Pętla kończy się bez wyjątku (DA-003) |

Gwarancja dostarczenia: **at-least-once** — paczka ponowiona po timeoucie mogła już zostać zapisana; API pomija duplikaty (DA-001, AC-05). Paczki nie są buforowane w workerze — przy dłuższej niedostępności API dane czekają w buforze agenta (ok. 22 min w symulatorze); po jego przepełnieniu obowiązują reguły `OUT_OF_RANGE` z DA-002.

### Mapowanie `ObservationBatch` → `ObservationBatchRequest`

| Kontrakt (`Contracts/`) | Źródło (model workera) |
|---|---|
| `deviceId` | `DeviceName` |
| `instanceId` | `InstanceId` |
| `observations[i].dataItemId`, `sequence`, `timestamp` | `DataItemId`, `Sequence`, `Timestamp` (UTC, z przesunięciem `+00:00`) |
| `observations[i].category` | `Category` (`Sample`/`Event`/`Condition` — nazwa 1:1) |
| `observations[i].type`, `subType`, `value` | `Type`, `SubType`, `Value` |
| `observations[i].conditionLevel` | `ConditionLevel` (`Normal`/`Warning`/`Fault`/`Unavailable` — nazwa 1:1) |
| `observations[i].units` (**nowe**) | `Units` (z `/probe`) |
| `observations[i].nativeCode` (**nowe**) | `NativeCode` (kod alarmu CONDITION) |

Mapowanie jawne (metoda rozszerzająca), bez refleksji.

### Rozszerzenie kontraktu API (decyzja PO)

- `ObservationDto` i `StoredObservationDto`: nowe **opcjonalne** pola `units` (string?, max 64 znaki) i `nativeCode` (string?, max 64 znaki). Brak pól w żądaniu = `null` — paczki w formacie DA-001 pozostają poprawne.
- API zapisuje i zwraca oba pola (`GET /api/v1/observations`). Walidacja długości w `ObservationBatchValidator` (błąd `observations[i].units` / `observations[i].nativeCode`).
- `nativeCode` dozwolony dla każdej kategorii (MTConnect podaje go tylko dla CONDITION, ale API nie wymusza).

## Dane i kontrakty

### Konfiguracja workera — `DataAcquisition:Api` (Options, walidacja przy starcie)

| Klucz | Typ | Domyślnie | Reguła |
|---|---|---|---|
| `BaseUrl` | URI | `http://localhost:5100` | wymagany, absolutny `http`/`https` |
| `ApiKey` | string | — | wymagany, min. 32 znaki (ta sama nazwa i reguła co w API — DA-001) |
| `RequestTimeout` | TimeSpan | `00:00:10` | 1 s – 5 min |

Lokalnie klucz w User Secrets **projektu Worker** (`dotnet user-secrets set "DataAcquisition:Api:ApiKey" "<ten sam klucz co API>" --project Worker/DataAcquisition.Worker.csproj`), na serwerze `DataAcquisition__Api__ApiKey`. Błąd walidacji → `OptionsValidationException` z pełnym kluczem (bez wartości).

### HTTP

- Named `HttpClient` „DataAcquisitionApi” z `IHttpClientFactory` (jak klient MTConnect w DA-002), `BaseAddress` z końcowym `/`, `Timeout` = `RequestTimeout`, `RemoveAllLoggers()` (logi żądań nie mogą zawierać nagłówka z kluczem i zagłuszać logów).
- Nagłówek `X-Api-Key` ustawiany na każdym żądaniu z Options.
- Serializacja: `System.Text.Json` z ustawieniami web (camelCase), enumy jako stringi (konwerter z `Contracts/`).

## Dostęp i interfejsy

- Worker staje się klientem API; nie wystawia żadnego portu.
- Logi: `Information` — wynik wysyłki (liczby, sekwencje, identyfikatory); `Error` — 400 (nazwy pól) i 401/403; nigdy wartości obserwacji ani klucza API.
- Domyślny odbiorca w DI: `ApiObservationSink` (zastępuje `LoggingObservationSink`, który zostaje usunięty).

## Kryteria odbioru

| AC | Przypadek testowy / Wejście | Oczekiwany wynik |
|---|---|---|
| AC-01 | `dotnet build`; `dotnet test DataAcquisition.slnx --nologo`; `dotnet format --verify-no-changes`; `dotnet tool run slopwatch analyze --fail-on warning` | Kod 0; slopwatch 0 znalezisk względem pustego baseline |
| AC-02 | Odbiorca + fałszywy handler: URL, metoda, nagłówek `X-Api-Key`, treść JSON (wszystkie pola, w tym `units`, `nativeCode`, czas UTC). Test end-to-end: odbiorca → API w procesie testowym (`WebApplicationFactory`) → `GET` | Żądanie zgodne z kontraktem; obserwacje zwrócone przez API 1:1, z `units` i `nativeCode` |
| AC-03 | Fałszywy handler: brak połączenia, timeout, `500`, `503`, `404`; test end-to-end z przerwą API (handler odrzucający N żądań, potem przepuszczający) + synchronizator z fałszywym agentem | Odbiorca rzuca wyjątek; synchronizator ponawia tę samą paczkę z backoffem; po „powrocie” API komplet obserwacji bez duplikatów |
| AC-04 | `400` z `ValidationProblemDetails`; `401`; `403` | `400`: log `Error` z nazwami pól, bez wartości, brak wyjątku. `401`/`403`: log `Error` + wyjątek (ponowienie). W żadnym logu nie ma wartości klucza ani obserwacji |
| AC-05 | Start workera bez klucza, z kluczem < 32 znaki, z `BaseUrl` względnym / `ftp://`, z `RequestTimeout` 0; przegląd logów testów pod kątem klucza | `OptionsValidationException` z pełnym kluczem konfiguracji; klucz nie występuje w logach |
| AC-06 | Ręcznie: symulator (`--seed 42`) + API + worker ≥ 2 min, w trakcie zatrzymanie API na ~20 s i ponowny start (dane API w pamięci giną przy restarcie — patrz Ryzyka); porównanie liczby obserwacji w API z sumą `accepted` z logów workera po restarcie API | Liczby równe; brak luk w sekwencjach w danych API dla czasu po restarcie API; w logach workera ponowienia w trakcie przerwy |

## Ryzyka i rozstrzygnięcia

- **Restart API kasuje dane** (store w pamięci z DA-001) — test AC-06 porównuje dane od momentu restartu API; trwała baza to osobny temat.
- **Pominięcie paczki przy 400** oznacza utratę jej danych — świadoma decyzja PO (alternatywa blokowałaby strumień). Ryzyko ograniczone: mapowanie jest testowane, a limity API (64/1024 znaki) są dalekie od wartości MTConnect; log `Error` pozwala wykryć problem.
- **Ponawianie 401** może trwać do przepełnienia bufora agenta — operator widzi `Error` co ≤ 30 s.
- **Zmiana kontraktu** — pola opcjonalne, nazwy camelCase; stare paczki (bez pól) przyjmowane bez zmian; test w `Api.Tests` dla paczki bez nowych pól.
- **Ten sam klucz w dwóch projektach** (API i Worker mają osobne User Secrets) — instrukcja w `release.md`; walidacja przy starcie wykryje brak.
- **Alternatywa odrzucona**: lokalny bufor na dysku w workerze (gwarancja przy długiej niedostępności API) — osobny temat.
