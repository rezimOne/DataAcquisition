# Wydanie: DA-001

Status: not-released. Commit: d1d9f5d. Wersja paczki / tag: <wersja — nadaje release manager>. Środowisko docelowe: <staging / prod — do ustalenia; DA-001 dostarcza API na etap deweloperski>.

## Zawartość wydania

- **Nowa usługa** `DataAcquisition.Api` (ASP.NET Core Minimal API, .NET 10) — projekt `Api/`, zależny od biblioteki `Contracts/` (`DataAcquisition.Contracts`).
- **Endpointy**: `POST /api/v1/observations`, `GET /api/v1/observations`, `GET /health`.
- **Konfiguracja w repozytorium**: `Api/appsettings.json` (poziomy logów, `AllowedHosts`), `Api/appsettings.Development.json` — **bez sekretów**.
- **Worker** (`DataAcquisition.Worker`) — bez zmian.
- **Baza danych / migracje** — brak (dane w pamięci procesu).

Budowanie paczki: `dotnet publish Api/DataAcquisition.Api.csproj -c Release -o publish/api` (katalog `publish/` jest w `.gitignore`).

## Wymagana konfiguracja

| Zmienna środowiskowa | Klucz konfiguracji | Wymagana | Opis |
|---|---|---|---|
| `DataAcquisition__Api__ApiKey` | `DataAcquisition:Api:ApiKey` | **tak** | Klucz API sprawdzany w nagłówku `X-Api-Key`. Min. 32 znaki; zalecany format `daq-api-` + ≥ 48 losowych znaków hex. Bez niej proces **nie startuje** (`OptionsValidationException: Configuration 'DataAcquisition:Api:ApiKey' is required…`). |
| `ASPNETCORE_URLS` | — | nie | Adres nasłuchu, np. `http://0.0.0.0:5100`. Domyślnie wg Kestrel; lokalnie `launchSettings.json` → `http://localhost:5100`. |
| `ASPNETCORE_ENVIRONMENT` | — | nie | Poza deweloperką **nie** ustawiać `Development` (User Secrets i logi `Debug` tylko lokalnie). |

Zasady:
- Klucza **nie** wpisywać do `appsettings*.json`, `launchSettings.json` ani repozytorium; na serwerze ustawić tylko w konfiguracji usługi/kontenera (nie globalnie dla maszyny, jeśli to możliwe).
- Ta sama wartość klucza będzie potrzebna workerowi (klient API, kolejny temat).
- Lokalnie: `dotnet user-secrets set "DataAcquisition:Api:ApiKey" "<klucz>" --project Api`.

## Wymagania wstępne i dowody przed wdrożeniem

- [ ] Dowody z `verification.md` zatwierdzone.
- [ ] Kopia zapasowa bazy danych / stanu wykonana — **nie dotyczy** (brak trwałych danych).
- [ ] `DataAcquisition__Api__ApiKey` skonfigurowana w środowisku docelowym (min. 32 znaki).
- [ ] Port usługi nie koliduje z agentem MTConnect (symulator domyślnie `:5000`).

## Przebieg wdrożenia i upoważnienie

- **Autoryzacja produkcji (release manager)**: <osoba, data> — inna osoba niż autor zmiany; agent AI nie autoryzuje wdrożeń.
- **Wykonujący wdrożenie**: <osoba / pipeline>
- **Data i okno serwisowe**: <data, godzina>
- **Kroki wdrożenia**: <do uzupełnienia przez release managera — sposób uruchamiania (usługa Windows / systemd / kontener) nie jest jeszcze ustalony (`ANALIZA-PROJEKTU.md`)>. Minimalnie: publish → ustawienie zmiennej → start → `curl http://<host>:<port>/health` zwraca `Healthy`.

## Procedura wycofania (Rollback)

- Zatrzymać / nie uruchamiać procesu `DataAcquisition.Api` albo przywrócić poprzednie binaria — worker nie zależy jeszcze od API, więc wycofanie nie wpływa na inne komponenty.
- Brak migracji i trwałych danych do odtworzenia; obserwacje w pamięci giną przy zatrzymaniu (zaakceptowane w spec).
- Kryterium wycofania: proces nie startuje mimo poprawnej konfiguracji klucza albo `/health` nie zwraca `200`.

## Obserwacja powdrożeniowa (Monitoring)

- `GET /health` → `200 Healthy`.
- Logi: `Information` „Observation batch from … accepted/duplicates” (ruch od workera), `Warning` „Rejected … missing or invalid API key” (błędny klucz klienta / próby dostępu), `Warning` „Duplicate observation … differs” (niespójne dane od klienta).
- Pamięć procesu — rośnie liniowo z liczbą obserwacji (brak retencji); obserwować przy dłuższej pracy.
- Po okresie obserwacji: status tematu `observed`, następnie `closed` (w `intent.md` i `BACKLOG.md`).
