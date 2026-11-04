# Intent: DA-002 — Klient MTConnect w workerze

ID: DA-002. Status: verified. Data: 2026-11-04. Odpowiedzialny: nog_programista2. Baza: 417e369.

## Problem i dowód

**Fakt**: `Worker/Worker.cs` to nadal szkielet z szablonu — co 1 s loguje `Worker running at…` i nie łączy się z niczym. Agent MTConnect (symulator `resources/cnc_mtconnect_sim.py`, `http://localhost:5000`) udostępnia `/probe`, `/current`, `/sample`, a API odbioru danych (DA-001, zmergowane w PR #1) jest gotowe — brakuje ogniwa, które pobiera dane z maszyny.

**Fakt (zachowanie agenta, z kodu symulatora)**: obserwacje w XML są grupowane po komponentach, więc kolejność elementów ≠ kolejność `sequence`; SAMPLE trafia do bufora co tick (5 Hz), EVENT/CONDITION tylko przy zmianie; bufor kołowy 131072 — `from` spoza bufora daje `MTConnectError` `OUT_OF_RANGE` (HTTP 404); restart agenta zmienia `Header/@instanceId`; brak nowych danych = pusty `/sample` z tym samym `nextSequence`.

## Oczekiwany rezultat

Worker nieprzerwanie i bez luk pobiera obserwacje z agenta MTConnect zgodnie z protokołem (`/probe` → `/current` → `/sample?from=nextSequence` w pętli), poprawnie reaguje na restart agenta, wypadnięcie z bufora i niedostępność agenta, a każdą paczkę obserwacji przekazuje dalej przez abstrakcję odbiorcy (`IObservationSink`). W tym temacie odbiorca loguje statystyki paczek; wysyłka do API to kolejny temat (DA-003), który podmieni tylko implementację odbiorcy.

## Użytkownicy, systemy i ograniczenia

- **Dotknięci użytkownicy / role**: operator/deweloper uruchamiający worker; pośrednio konsumenci danych w API (DA-003).
- **Dotknięte systemy / integracje**: `Worker/` (nowy klient, parser, pętla synchronizacji, konfiguracja), `Worker.Tests/`; agent MTConnect (tylko odczyt HTTP). API i `Contracts/` — bez zmian.
- **Ograniczenia**: .NET 10, xUnit v3; MTConnect 1.3 (namespace `urn:mtconnect.org:MTConnectStreams:1.3`); czas przez `TimeProvider` (testy na `FakeTimeProvider`, zgodnie z istniejącymi testami workera); `HttpClient` przez `IHttpClientFactory`; zero sekretów (agent nie wymaga uwierzytelnienia).

## Zakres i poza zakresem

- **W zakresie**:
  - typed `HttpClient` do agenta: `GET /probe`, `/current`, `/sample?from=&count=`, z timeoutem,
  - parser XML (`MTConnectDevices`, `MTConnectStreams`, `MTConnectError`) → model obserwacji: urządzenie, `instanceId`, `dataItemId`, `sequence`, `timestamp` (UTC), kategoria, `type`/`subType` (z `/probe`), wartość (w tym `UNAVAILABLE`), poziom CONDITION,
  - pętla synchronizacji z obsługą: zmiany `instanceId` (resync przez `/current`), `OUT_OF_RANGE` (resync + ostrzeżenie o luce), pustego `/sample`, niedostępności agenta / 5xx / błędnego XML (ponowienia z rosnącym opóźnieniem, worker nie kończy pracy),
  - uporządkowanie obserwacji paczki po `sequence` przed przekazaniem dalej,
  - `IObservationSink` + implementacja logująca (liczba obserwacji, zakres sekwencji, urządzenie),
  - konfiguracja przez Options z walidacją przy starcie (adres agenta, interwał odpytywania, `count`, timeout),
  - zastąpienie szkieletu `Worker.cs` i jego testów (obecne 3 testy sprawdzają log „Worker running at” — przestaną mieć sens).
- **Poza zakresem**:
  - wysyłka do API (`DataAcquisition.Api`) — DA-003,
  - trwałe zapamiętanie ostatniej sekwencji między restartami workera (po restarcie start od `/current`),
  - strumień `multipart/x-mixed-replace` (`/sample?interval=`), adapter SHDR, wiele agentów naraz, uwierzytelnianie do agenta,
  - zmiany w symulatorze.

## Kryteria sukcesu (Acceptance Criteria)

- AC-01: `dotnet build DataAcquisition.slnx`, `dotnet test DataAcquisition.slnx` i `dotnet format DataAcquisition.slnx --verify-no-changes` kończą się kodem 0.
- AC-02: Parser zamienia odpowiedzi `/current` i `/sample` (fixtures XML w formacie agenta, w tym CONDITION `Normal`/`Fault`, wartość `UNAVAILABLE`, puste `/sample`) na obserwacje z poprawnymi polami oraz nagłówek (`instanceId`, `nextSequence`, `firstSequence`, `lastSequence`); `type`/`subType` pochodzą z `/probe`; czas w UTC.
- AC-03: Po starcie worker wykonuje `/probe` → `/current` → `/sample?from=<nextSequence>` i w kolejnych cyklach używa `nextSequence` z poprzedniej odpowiedzi; przy ciągłej pracy każda obserwacja trafia do odbiorcy dokładnie raz i w kolejności `sequence` (brak luk i duplikatów w ramach jednej instancji agenta).
- AC-04: Zmiana `Header/@instanceId` powoduje resynchronizację przez `/current` i log `Information`; dane nowej instancji są pobierane dalej bez restartu workera.
- AC-05: Odpowiedź `OUT_OF_RANGE` powoduje resynchronizację przez `/current` i log `Warning` z wielkością utraconego zakresu sekwencji; worker działa dalej.
- AC-06: Niedostępny agent, timeout, HTTP 5xx lub niepoprawny XML → log `Warning`, ponowienie z rosnącym opóźnieniem (z górnym limitem); po powrocie agenta pobieranie wznawia się bez restartu workera; host nie kończy pracy.
- AC-07: Konfiguracja (adres agenta, interwał, `count`, timeout) ładowana przez Options z walidacją przy starcie — błędna wartość zatrzymuje start z komunikatem wskazującym klucz; zatrzymanie hosta (Ctrl+C / `StopAsync`) kończy pętlę bez wyjątku.
- AC-08: Test ręczny z symulatorem (`--seed 42`, min. 2 minuty): ciągłość sekwencji w logach odbiorcy, w tym po restarcie symulatora w trakcie pracy workera (zmiana `instanceId`).

## Pytania i decyzja

| Pytanie | Decyzja | Kto, kiedy |
|---|---|---|
| Czy przy starcie/resynchronizacji przekazywać migawkę z `/current` jako obserwacje? | Tak — duplikaty odrzuci API po (`deviceId`, `instanceId`, `sequence`) | Product owner, 2026-11-04 |
| Co robi odbiorca w DA-002? | Loguje statystyki paczek (`IObservationSink`); wysyłka do API w DA-003 | Product owner, 2026-11-04 |
| Domyślne parametry | Interwał 1 s, `count` 1000, timeout HTTP 10 s, backoff 1 s → max 30 s | Product owner, 2026-11-04 |
| Klucz konfiguracji | `DataAcquisition:MTConnect:*` | Tech lead (rekomendacja przyjęta wraz z intent) |
| Model obserwacji w workerze | Własny model w `Worker/`; mapowanie na `ObservationDto` w DA-003 | Tech lead (rekomendacja przyjęta wraz z intent) |

- **Decyzja product ownera**: zaakceptowano — nog_programista2, 2026-11-04. Status tematu: `ready`.
