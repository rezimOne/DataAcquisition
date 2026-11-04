# Analiza Projektu i Architektura: DataAcquisition

Dokument stanowi punkt wejścia dla każdego programisty i asystenta AI rozpoczynającego pracę w tym repozytorium.

---

## 1. Cel i przeznaczenie systemu

DataAcquisition to usługa zbierania danych z obrabiarki CNC przez protokół **MTConnect**. Worker cyklicznie odpytuje agenta MTConnect (HTTP/XML), odczytuje obserwacje (pozycje osi, obciążenia, temperatury, posuw, obroty wrzeciona, stan wykonania programu, alarmy) i przekazuje je do docelowego **API**, które udostępnia dane dalszym systemom (monitoring, analityka, OEE).

Na etapie wytwarzania źródłem danych jest symulator `resources/cnc_mtconnect_sim.py`, emulujący 5-osiowe centrum obróbkowe (klasa Mazak VARIAXIS/INTEGREX: osie X, Y, Z, B, C + wrzeciono S) wraz z agentem MTConnect.

**Stan obecny**: repozytorium zawiera szkielet workera (szablon `dotnet new worker` — pętla logująca co 1 s) i jego testy. Klient MTConnect, parser XML, model danych i klient API są do zaimplementowania w kolejnych tematach.

---

## 2. Architektura techniczna i stos technologiczny

- **Backend / Usługi**: .NET 10 (`net10.0`), Worker Service (`Microsoft.NET.Sdk.Worker`, Generic Host, `BackgroundService`), C# z `Nullable` i `ImplicitUsings`.
- **Frontend / Klienci**: brak — usługa bez interfejsu użytkownika.
- **Baza danych / Przechowywanie**: brak w workerze. Dane trafiają do API; ewentualny bufor lokalny na czas niedostępności API — do ustalenia (ADR).
- **Komunikacja i Integracje**:
  - wejście: agent MTConnect przez HTTP GET, odpowiedzi XML (MTConnect 1.3, namespace `urn:mtconnect.org:MTConnectStreams:1.3`);
  - wyjście: docelowe API przez HTTP — kontrakt (endpoint, format, uwierzytelnianie) **do ustalenia**;
  - opcjonalnie symulator wystawia surowy adapter SHDR na TCP (`--shdr <port>`) — worker z niego nie korzysta.
- **Środowisko uruchomieniowe**: proces konsolowy Generic Host (lokalnie `dotnet run`); docelowo usługa Windows / systemd / kontener — do ustalenia.
- **Testy**: xUnit v3, `FakeTimeProvider`, `FakeLogger`.

### Przepływ danych (docelowy)

```
┌──────────────────────┐  HTTP GET /probe,/current,/sample   ┌──────────────────────────┐  HTTP   ┌─────────┐
│ Agent MTConnect      │ ◄────────────────────────────────── │ DataAcquisition.Worker   │ ──────► │   API   │
│ (symulator :5000     │ ──────────────────────────────────► │ odpytywanie → parsowanie │         │         │
│  lub realna maszyna) │        XML (Devices/Streams)        │ → mapowanie → wysyłka    │         │         │
└──────────────────────┘                                     └──────────────────────────┘         └─────────┘
```

### Protokół pobierania danych z agenta MTConnect

1. `GET /probe` — model urządzenia (`MTConnectDevices`): komponenty i `dataItemId`.
2. `GET /current` — migawka ostatnich wartości; zapamiętaj `Header/@nextSequence` i `Header/@instanceId`.
3. `GET /sample?from=<nextSequence>&count=<N>` — w pętli, za każdym razem z nowym `nextSequence`.
4. Zmiana `Header/@instanceId` → agent został zrestartowany: wróć do kroku 2.
5. Błąd `OUT_OF_RANGE` (`MTConnectError`, HTTP 404) → `from` wypadł z bufora kołowego (domyślnie 131072 obserwacji): wróć do kroku 2 (utrata danych do zalogowania). Nieznana ścieżka → `UNSUPPORTED` (HTTP 404). Domyślne `count` = 100.

Istotne cechy agenta (wpływają na implementację):

- Kolejność elementów w XML **nie** jest kolejnością sekwencji (obserwacje są grupowane po komponentach). Synchronizacja wyłącznie przez `nextSequence`/`sequence`.
- `SAMPLE` trafia do bufora co tick (domyślnie 5 Hz, `--hz`); `EVENT` i `CONDITION` — tylko przy zmianie wartości.
- `PATH_FEEDRATE` w `MILLIMETER/SECOND` (w CSV/JSONL/SHDR symulatora — mm/min).
- Strumień `GET /sample?interval=<ms>` (`multipart/x-mixed-replace`) jest dostępny jako alternatywa dla odpytywania.

### Dane udostępniane przez symulator (urządzenie `VMC5AX`)

| Komponent | DataItemy (`id` → typ) |
|---|---|
| Device | `avail` → AVAILABILITY |
| Controller / Path | `exec` EXECUTION, `mode` CONTROLLER_MODE, `pgm` PROGRAM, `block` BLOCK, `tool` TOOL_NUMBER, `pcount` PART_COUNT, `feed` PATH_FEEDRATE; `system` CONDITION SYSTEM (alarmy) |
| Osie liniowe X, Y, Z | `{x,y,z}pos` POSITION [mm], `{x,y,z}load` LOAD [%], `{x,y,z}temp` TEMPERATURE [°C] |
| Osie obrotowe B, C | `{b,c}ang` ANGLE [°], `{b,c}load` LOAD [%], `{b,c}temp` TEMPERATURE [°C] |
| Wrzeciono S | `srpm` ROTARY_VELOCITY [obr/min], `sload` LOAD [%], `stemp` TEMPERATURE [°C] |

---

## 3. Kluczowe komponenty i struktura kodu

| Komponent / Katalog | Rola w systemie | Główne technologie |
|---|---|---|
| `DataAcquisition.slnx` | Solucja (format `.slnx`) | .NET SDK 10 |
| `Worker/` (`DataAcquisition.Worker`) | Usługa zbierania danych: `Program.cs` (kompozycja DI, rejestracja `TimeProvider.System` i hosted service), `Worker.cs` (`BackgroundService` z pętlą główną) | Generic Host, `Microsoft.Extensions.*` |
| `Worker/appsettings*.json` | Konfiguracja (obecnie tylko logowanie). Miejsce na adres agenta, interwały i adres API — przez wzorzec Options | `Microsoft.Extensions.Configuration` |
| `Worker.Tests/` (`DataAcquisition.Worker.Tests`) | Testy jednostkowe workera z deterministycznym czasem | xUnit v3, `FakeTimeProvider`, `FakeLogger` |
| `resources/cnc_mtconnect_sim.py` | Symulator CNC + agent MTConnect (HTTP), opcjonalnie eksport CSV/JSONL i adapter SHDR. Narzędzie deweloperskie, nie jest częścią wdrożenia | Python 3.9+, wyłącznie biblioteka standardowa |
| `docs/sdlc/` | Dokumentacja procesu i tematów (Spec-First) | Markdown |
| `tools/`, `.claude/`, `.githooks/` | Narzędzia i bramki procesu SDLC (zmienia tylko człowiek) | PowerShell |

### Planowany podział odpowiedzialności w workerze

Do potwierdzenia w planach tematów / ADR (`docs/sdlc/02-design/DECYZJE.md`):

- **Klient MTConnect** — `HttpClient` (typed client przez `IHttpClientFactory`), wywołania `/probe`, `/current`, `/sample`.
- **Parser MTConnect** — XML → model obserwacji (`DataItemId`, `Timestamp`, `Sequence`, wartość, kategoria); obsługa `MTConnectError`.
- **Stan synchronizacji** — `instanceId`, `nextSequence`, reakcja na restart agenta i `OUT_OF_RANGE`.
- **Klient API** — wysyłka paczek obserwacji, ponowienia z backoffem.
- **Worker** — orkiestracja pętli, interwały przez `TimeProvider`, poprawne zatrzymanie na `CancellationToken`.

---

## 4. Ograniczenia architektoniczne i "Złote Zasady"

1. **Testowalność czasu**: Żadnych `DateTime.Now`, `Thread.Sleep` ani `Task.Delay` bez `TimeProvider` — czas wstrzykiwany przez DI, w testach `FakeTimeProvider`.
2. **Synchronizacja po sekwencji**: Kolejność i ciągłość danych wyznacza wyłącznie `sequence`/`nextSequence` i `instanceId` agenta, nigdy pozycja elementu w XML.
3. **Odporność**: Niedostępność agenta lub API nie może zatrzymać hosta — loguj (`Warning` dla oczekiwanych awarii sieci, `Error` dla nieoczekiwanych), ponawiaj z backoffem, honoruj `CancellationToken`.
4. **Konfiguracja zamiast stałych**: Adres agenta, interwały, `count`, adres API — w `appsettings.json` przez wzorzec Options z walidacją przy starcie.
5. **Bezpieczeństwo**: Nigdy nie umieszczaj sekretów, haseł ani tokenów API w repozytorium ani logach — lokalnie User Secrets (`UserSecretsId` jest skonfigurowany), produkcyjnie zmienne środowiskowe / magazyn sekretów.
6. **Obsługa błędów**: Brak nowych obserwacji (pusty `/sample`) to nie błąd. `UNAVAILABLE` w obserwacji MTConnect to poprawna wartość (brak danych), nie awaria.
