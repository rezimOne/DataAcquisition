# Intent: DA-004 — Wysylka obserwacji z workera do API

ID: DA-004. Status: ready. Data: 2026-11-04. Odpowiedzialny: nog_programista2. Baza: 751df55.

## Problem i dowód

**Fakt**: worker (DA-002) pobiera obserwacje z agenta MTConnect, ale odbiorca `LoggingObservationSink` tylko loguje statystyki paczek (`Worker/MTConnect/ObservationSinks.cs`). API (DA-001) przyjmuje paczki pod `POST /api/v1/observations` z kluczem `X-Api-Key`, lecz nikt ich nie wysyła — dane z maszyny nie trafiają do API. Przepływ symulator → worker → API jest przerwany na ostatnim odcinku.

**Fakt (gotowe elementy)**: synchronizator dostarcza paczki at-least-once i ponawia tę samą paczkę z rosnącym opóźnieniem, gdy odbiorca rzuci wyjątek (DA-002, AC-06); API pomija duplikaty po (`deviceId`, `instanceId`, `sequence`) (DA-001, AC-05); paczka ma ≤ 1000 obserwacji jednego urządzenia (`SampleCount` = limit API); kontrakt DTO jest w `Contracts/` (`ObservationBatchRequest`, `ObservationDto`, `ApiContract`).

**Otwarte z poprzednich tematów**: `NativeCode` (kod alarmu, np. `AL-0123`) i `Units` są w modelu workera, ale nie w kontrakcie API (DA-002); API przyjmuje `timestamp` bez strefy jako czas lokalny serwera (DA-001, odstępstwo 2).

## Oczekiwany rezultat

Każda paczka obserwacji pobrana przez worker trafia do API. Gdy API jest niedostępne lub odpowiada błędem przejściowym, worker nie gubi danych (paczka jest ponawiana, dopóki agent ma ją w buforze), a po powrocie API wznawia wysyłkę bez restartu. Po zmianie dane w API są kompletne i bez duplikatów dla ciągłej pracy symulatora.

## Użytkownicy, systemy i ograniczenia

- **Dotknięci użytkownicy / role**: konsumenci danych w API; operator workera (konfiguracja adresu i klucza API).
- **Dotknięte systemy / integracje**: `Worker/` (nowy odbiorca HTTP do API, mapowanie na kontrakt, konfiguracja), `Worker.Tests/`, ewentualnie `Contracts/` i `Api/` (jeśli kontrakt zostanie rozszerzony — decyzja niżej).
- **Ograniczenia**: .NET 10, xUnit v3; slopwatch aktywny (baseline pusty — zero nowych znalezisk); klucz API tylko w User Secrets / zmiennej środowiskowej (`DataAcquisition:Api:ApiKey` — ta sama nazwa co w API), nigdy w repozytorium ani logach; `HttpClient` przez `IHttpClientFactory`.

## Zakres i poza zakresem

- **W zakresie**:
  - odbiorca `IObservationSink` wysyłający paczkę do `POST /api/v1/observations` (zastępuje odbiorcę logującego jako domyślny),
  - mapowanie `ObservationBatch` → `ObservationBatchRequest` z `Contracts/` (worker zaczyna referencjonować `Contracts`),
  - konfiguracja workera `DataAcquisition:Api:*` (adres API, klucz, timeout) z walidacją przy starcie,
  - klasyfikacja odpowiedzi API: sukces / błąd przejściowy (ponowienie) / błąd trwały (zachowanie — decyzja niżej),
  - log wyniku wysyłki (`accepted`, `duplicates`) bez wartości obserwacji i bez klucza,
  - testy jednostkowe odbiorcy (fałszywy handler HTTP) i test end-to-end: worker → prawdziwe API w procesie testowym (`WebApplicationFactory`),
  - test ręczny: symulator + API + worker, porównanie liczby obserwacji w API z logami workera.
- **Poza zakresem**:
  - trwała baza w API i lokalny bufor na dysku w workerze (osobne tematy),
  - zmiana sposobu pobierania z agenta (DA-002),
  - uwierzytelnianie inne niż klucz API, HTTPS/certyfikaty, wdrożenie.

## Kryteria sukcesu (Acceptance Criteria)

- AC-01: `dotnet build`, `dotnet test DataAcquisition.slnx --nologo`, `dotnet format --verify-no-changes` i `dotnet tool run slopwatch analyze --fail-on warning` kończą się kodem 0.
- AC-02: Paczka z workera jest wysyłana do API z nagłówkiem `X-Api-Key`; pola obserwacji (urządzenie, `instanceId`, `dataItemId`, `sequence`, czas UTC, kategoria, `type`, `subType`, wartość, poziom CONDITION) są zmapowane 1:1 na kontrakt; po wysyłce obserwacje są zwracane przez `GET /api/v1/observations`.
- AC-03: API niedostępne, timeout lub HTTP 5xx → odbiorca zgłasza niepowodzenie, synchronizator ponawia tę samą paczkę z rosnącym opóźnieniem, po powrocie API paczka jest dostarczona; brak utraty i duplikatów w API.
- AC-04: Błędy trwałe (np. 400 — niepoprawna paczka, 401 — zły klucz) są obsłużone zgodnie z decyzją PO (niżej), z logiem na poziomie `Error`, bez wartości obserwacji i bez klucza.
- AC-05: Konfiguracja `DataAcquisition:Api:*` ładowana przez Options z walidacją przy starcie (brak/za krótki klucz, zły adres → start przerwany z pełnym kluczem w komunikacie); klucz nie pojawia się w logach.
- AC-06: Test ręczny ≥ 2 min (symulator + API + worker): liczba obserwacji zwróconych przez API dla urządzenia = liczba unikalnych obserwacji przekazanych przez worker; zatrzymanie i ponowne uruchomienie API w trakcie nie powoduje luki.

## Pytania i decyzja

| Pytanie | Decyzja | Kto, kiedy |
|---|---|---|
| Błąd trwały API | **401/403**: jak przejściowy (ponawianie z backoffem) + log `Error`. **400**: log `Error` z polami walidacji (bez wartości) i pominięcie paczki | Product owner, 2026-11-04 |
| `NativeCode` i `Units` w kontrakcie API | Tak — oba jako opcjonalne pola (zmiana kompatybilna wstecz) | Product owner, 2026-11-04 |
| Wymuszanie strefy czasowej w `timestamp` | Poza zakresem (worker zawsze wysyła czas z przesunięciem) | Product owner, 2026-11-04 |
| Odbiorca logujący | Usunięty jako domyślny; statystyki wysyłki loguje nowy odbiorca | Tech lead (rekomendacja przyjęta wraz z intent) |
| Konfiguracja | `DataAcquisition:Api:BaseUrl` (domyślnie `http://localhost:5100`), `:ApiKey` (wymagany, min. 32 znaki), `:RequestTimeout` (10 s) | Tech lead (rekomendacja przyjęta wraz z intent) |

- **Decyzja product ownera**: zaakceptowano — nog_programista2, 2026-11-04. Status tematu: `ready`.
