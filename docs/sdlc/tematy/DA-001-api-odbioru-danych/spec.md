# Spec: DA-001 — API odbioru danych z maszyn

Status: approved. Odpowiedzialny: nog_programista2. Data: 2026-11-04. Źródło: [intent](intent.md).

## Zachowanie

### Zapis paczki obserwacji — `POST /api/v1/observations`

1. **Happy path**: Worker wysyła paczkę z jednego urządzenia i jednej instancji agenta (`deviceId`, `instanceId` z `Header/@instanceId`) zawierającą 1–1000 obserwacji. API waliduje całą paczkę, zapisuje nowe obserwacje i zwraca `200 OK` z liczbą przyjętych i pominiętych.
2. **Atomowość walidacji**: Jeśli choć jedna obserwacja jest niepoprawna — cała paczka jest odrzucana (`400`), nic nie jest zapisywane.
3. **Idempotencja**: Klucz obserwacji = (`deviceId`, `instanceId`, `sequence`). Obserwacja o istniejącym kluczu jest pomijana i liczona jako duplikat (pierwszy zapis wygrywa). Jeśli duplikat ma inną treść niż zapisana obserwacja — pozostaje pominięty, a API loguje `Warning` (bez treści wartości). Ponowienie całej paczki po timeoucie po stronie workera jest więc bezpieczne.
4. **Duplikat `sequence` wewnątrz jednej paczki** → `400` (paczka niespójna).
5. **Współbieżność**: Repozytorium jest bezpieczne wątkowo. Dwie równoległe paczki z tymi samymi kluczami → każda obserwacja zapisana dokładnie raz; suma `accepted` z obu odpowiedzi = liczba unikalnych kluczy.
6. **Brak / błędny klucz API** → `401`, walidacja treści nie jest wykonywana, nic nie jest zapisywane.
7. **Niepoprawny JSON / zły `Content-Type`** → `400` (`ProblemDetails`).

### Odczyt obserwacji — `GET /api/v1/observations`

1. Parametry: `deviceId` (wymagany), `from` (opcjonalny, włącznie), `to` (opcjonalny, wyłącznie), `limit` (opcjonalny, domyślnie 100, zakres 1–1000).
2. Filtr czasu dotyczy `timestamp` obserwacji. `from >= to` → `400`.
3. Sortowanie: `instanceId` rosnąco, potem `sequence` rosnąco. Zwracanych jest pierwszych `limit` wyników; `truncated: true`, gdy pasujących jest więcej.
4. Nieznane `deviceId` lub brak wyników → `200` z pustą listą (brak danych to nie błąd).

### Health check — `GET /health`

`200` z treścią `Healthy`, bez uwierzytelnienia. Nie ujawnia konfiguracji ani wersji.

### Start aplikacji

Brak lub zbyt krótki klucz API w konfiguracji (`DataAcquisition:Api:ApiKey`, min. 32 znaki) → aplikacja nie startuje (walidacja Options przy starcie, `ValidateOnStart`), komunikat wskazuje nazwę klucza konfiguracji, nie wartość.

## Dane i kontrakty

JSON, nazwy pól `camelCase`, enumy jako stringi. Czas: ISO 8601 z przesunięciem (`DateTimeOffset`), przechowywany i zwracany w UTC. Kontrakty w projekcie `DataAcquisition.Contracts` (bez zależności od ASP.NET), współdzielone z workerem w kolejnych tematach.

### `ObservationBatchRequest` (POST, body)

| Pole | Typ | Wymagane | Reguła |
|---|---|---|---|
| `deviceId` | string | tak | niepusty, max 64 znaki (np. `VMC5AX` z `/probe`) |
| `instanceId` | int64 | tak | > 0 (`Header/@instanceId` agenta) |
| `observations` | `ObservationDto[]` | tak | 1–1000 elementów, `sequence` unikalne w paczce |

### `ObservationDto`

| Pole | Typ | Wymagane | Reguła |
|---|---|---|---|
| `dataItemId` | string | tak | niepusty, max 64 znaki (np. `xpos`, `exec`, `system`) |
| `sequence` | int64 | tak | ≥ 1 |
| `timestamp` | DateTimeOffset | tak | poprawna data z przesunięciem |
| `category` | enum `Sample` / `Event` / `Condition` | tak | jedna z wartości |
| `type` | string | nie | max 64 znaki (np. `POSITION`, `EXECUTION`) |
| `subType` | string | nie | max 64 znaki (np. `ACTUAL`) |
| `value` | string | tak | max 1024 znaki; liczby jako tekst w notacji niezmiennej kultury (`12.345`); `UNAVAILABLE` to poprawna wartość, nie błąd |
| `conditionLevel` | enum `Normal` / `Warning` / `Fault` / `Unavailable` | tylko dla `Condition` | wymagane dla `Condition`, zabronione dla pozostałych kategorii |

Przykład:

```json
{
  "deviceId": "VMC5AX",
  "instanceId": 1793790000,
  "observations": [
    { "dataItemId": "xpos", "sequence": 1201, "timestamp": "2026-11-04T10:15:30.120Z",
      "category": "Sample", "type": "POSITION", "subType": "ACTUAL", "value": "112.457" },
    { "dataItemId": "system", "sequence": 1202, "timestamp": "2026-11-04T10:15:30.120Z",
      "category": "Condition", "type": "SYSTEM", "value": "SPINDLE OVERLOAD", "conditionLevel": "Fault" }
  ]
}
```

### `ObservationBatchResponse` (POST, `200`)

```json
{ "accepted": 2, "duplicates": 0 }
```

### `ObservationQueryResponse` (GET, `200`)

```json
{ "items": [ { "deviceId": "VMC5AX", "instanceId": 1793790000, "dataItemId": "xpos", "sequence": 1201,
               "timestamp": "2026-11-04T10:15:30.12+00:00", "category": "Sample", "type": "POSITION",
               "subType": "ACTUAL", "value": "112.457", "conditionLevel": null } ],
  "truncated": false }
```

### Błędy

`ProblemDetails` (RFC 9457, `application/problem+json`). Błędy walidacji: `ValidationProblemDetails` z `errors` kluczowanym ścieżką pola, np. `observations[3].dataItemId`.

### Przechowywanie

Interfejs `IObservationStore` (zapis paczki zwracający `accepted`/`duplicates`, zapytanie z filtrem). Implementacja w pamięci, singleton, bezpieczna wątkowo; dane giną po restarcie procesu (świadomie — patrz Ryzyka).

## Dostęp i interfejsy

- **Uwierzytelnianie**: nagłówek `X-Api-Key` wymagany dla całej grupy `/api/v1`. Porównanie w czasie stałym (`CryptographicOperations.FixedTimeEquals`). Brak/błąd → `401` + `ProblemDetails` bez wskazówki, czy klucz był pusty czy błędny.
- **Konfiguracja klucza**: `DataAcquisition:Api:ApiKey` (nazwa z prefiksem projektu, ta sama sekcja posłuży klientowi API w workerze) — lokalnie User Secrets (`dotnet user-secrets set "DataAcquisition:Api:ApiKey" "<klucz>" --project Api`), produkcyjnie zmienna środowiskowa `DataAcquisition__Api__ApiKey`. Zalecany format wartości: prefiks `daq-api-` + losowe ≥ 48 znaków hex (rozpoznawalny w skanerach sekretów i rotacji). W `appsettings*.json` **brak** wartości. Testy używają jawnie fikcyjnego klucza ustawianego w `WebApplicationFactory`.
- **Role**: jedna rola (posiadacz klucza) dla zapisu i odczytu; rozdział ról poza zakresem.
- **Logowanie**: `Information` — przyjęta paczka (`deviceId`, `instanceId`, `accepted`, `duplicates`); `Warning` — odrzucony klucz (bez wartości nagłówka), duplikat o innej treści. Nigdy nie logujemy wartości klucza ani nagłówka `X-Api-Key`.
- **Port lokalny**: `http://localhost:5100` (`launchSettings.json`) — 5000 zajmuje symulator MTConnect.

## Kryteria odbioru

| AC | Przypadek testowy / Wejście | Oczekiwany wynik |
|---|---|---|
| AC-01 | `dotnet build DataAcquisition.slnx`; `dotnet test DataAcquisition.slnx` | Kod 0; solucja zawiera `DataAcquisition.Contracts`, `DataAcquisition.Api`, `DataAcquisition.Api.Tests`; istniejące testy workera nadal zielone |
| AC-02 | POST poprawnej paczki (2 obserwacje, w tym `Condition`) z poprawnym kluczem, następnie GET `?deviceId=VMC5AX` | POST `200` `{accepted:2, duplicates:0}`; GET zwraca obie obserwacje z identycznymi polami, czas w UTC |
| AC-03 | POST bez `X-Api-Key`; POST z błędnym kluczem; GET bez klucza | `401` `ProblemDetails`; następny GET z poprawnym kluczem zwraca pustą listę |
| AC-04 | POST: pusta lista; 1001 obserwacji; brak `deviceId`; pusty `dataItemId`; `sequence = 0`; `Condition` bez `conditionLevel`; `Sample` z `conditionLevel`; duplikat `sequence` w paczce; niepoprawny JSON; paczka z 1 poprawną i 1 niepoprawną obserwacją | `400` z `errors` wskazującym pole (dla JSON — `ProblemDetails`); GET nie zwraca żadnej obserwacji z tych paczek |
| AC-05 | Ta sama paczka wysłana dwukrotnie; paczka częściowo nakładająca się (2 stare + 1 nowa); 2 równoległe identyczne paczki | 2. wysłanie `{accepted:0, duplicates:N}`; nakładająca `{accepted:1, duplicates:2}`; równoległe: suma `accepted` = liczba unikalnych; GET bez duplikatów |
| AC-06 | Obserwacje 2 urządzeń i 2 `instanceId`; GET z `deviceId`, `from`/`to`, `limit=2`; GET `from >= to`; GET `limit=0` i `limit=1001`; GET bez `deviceId`; GET nieznanego urządzenia | Tylko obserwacje urządzenia z zakresu `[from, to)`, sortowanie (`instanceId`, `sequence`), max `limit`, `truncated` poprawne; błędne parametry → `400`; nieznane urządzenie → `200` pusta lista |
| AC-07 | GET `/health` bez klucza | `200`, `Healthy` |
| AC-08 | Start aplikacji bez `DataAcquisition:Api:ApiKey` oraz z kluczem krótszym niż 32 znaki; przegląd repozytorium i logów z testów | Start kończy się wyjątkiem walidacji Options wskazującym `DataAcquisition:Api:ApiKey`; brak wartości klucza w `appsettings*.json` i w przechwyconych logach (`FakeLogger`/sink testowy) |

## Ryzyka i rozstrzygnięcia

- **Dane w pamięci**: utrata po restarcie i nieograniczony wzrost pamięci przy długiej pracy. Akceptowalne na etap deweloperski (decyzja PO z intent). Trwała baza i retencja — osobny temat z ADR (`../../02-design/`). Interfejs `IObservationStore` izoluje zmianę.
- **Idempotencja przy restarcie API**: po restarcie (utrata danych) worker może dosłać obserwacje ponownie — zostaną przyjęte. Zgodne z założeniem; z trwałą bazą problem znika.
- **Kolizja `instanceId`**: MTConnect gwarantuje zmianę `instanceId` po restarcie agenta, więc `sequence` może się powtarzać tylko w nowej instancji — klucz z `instanceId` to obsługuje.
- **Walidacja**: ręczna walidacja w kodzie (jawne reguły i ścieżki pól) zamiast atrybutów DataAnnotations — reguła warunkowa `conditionLevel` i unikalność `sequence` nie wyrażają się prosto atrybutami. Alternatywa: wbudowana walidacja Minimal API .NET 10 — odrzucona dla czytelności reguł krzyżowych.
- **Uwierzytelnianie kluczem**: jeden współdzielony klucz to minimum; rotacja wymaga restartu API. OAuth/role — przyszły temat.
- **Kompatybilność**: nowe projekty, worker nie zmieniany — brak ryzyka regresji poza plikiem solucji.
