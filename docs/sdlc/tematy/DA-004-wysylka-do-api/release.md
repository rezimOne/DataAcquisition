# Wydanie: DA-004

Status: not-released. Commit: <commit po review — uzupełnia release manager>. Wersja paczki / tag: <wersja — nadaje release manager>. Środowisko docelowe: <staging / prod — do ustalenia>.

## Zawartość wydania

- **Worker** (`DataAcquisition.Worker`): wysyła paczki obserwacji do API (`POST /api/v1/observations`) zamiast je tylko logować. Nowa zależność: projekt `Contracts`.
- **API** (`DataAcquisition.Api`) i **kontrakt** (`DataAcquisition.Contracts`): opcjonalne pola `units` i `nativeCode` w obserwacjach (zapis, odczyt, walidacja ≤ 64 znaki). Zmiana kompatybilna wstecz — paczki bez tych pól są przyjmowane jak dotąd.
- **Testy**: nowy projekt `Integration.Tests` (nie jest wdrażany).
- **Baza danych / migracje** — brak (API nadal w pamięci).
- **Slopwatch**: `.slopwatch/baseline.json` (pusty, utworzony przez człowieka) wchodzi na `main` razem z tym tematem.

Budowanie paczek: `dotnet publish Api/DataAcquisition.Api.csproj -c Release -o publish/api` oraz `dotnet publish Worker/DataAcquisition.Worker.csproj -c Release -o publish/worker`.

## Wymagana konfiguracja

**Kolejność wdrożenia: najpierw API, potem worker.** Poprzednia wersja API nie odrzuci paczek z `units`/`nativeCode` (`System.Text.Json` domyślnie pomija nieznane pola), ale ich nie zapisze — dla kompletu danych API musi być zaktualizowane pierwsze.

| Zmienna środowiskowa (worker) | Klucz konfiguracji | Wymagana | Opis |
|---|---|---|---|
| `DataAcquisition__Api__ApiKey` | `DataAcquisition:Api:ApiKey` | **tak** | **Ten sam klucz co w API**. Min. 32 znaki. Bez niego worker **nie startuje** (`OptionsValidationException: DataAcquisition:Api:ApiKey is required…`) |
| `DataAcquisition__Api__BaseUrl` | `DataAcquisition:Api:BaseUrl` | na serwerze tak | Adres API, domyślnie `http://localhost:5100` (absolutny `http`/`https`, może zawierać ścieżkę) |
| `DataAcquisition__Api__RequestTimeout` | `DataAcquisition:Api:RequestTimeout` | nie | Domyślnie `00:00:10` (1 s – 5 min) |

Konfiguracja MTConnect (`DataAcquisition__MTConnect__*`) — bez zmian (DA-002).

Lokalnie (Development) worker czyta klucz z **własnych** User Secrets — osobnych niż API:

- `dotnet user-secrets set "DataAcquisition:Api:ApiKey" "<ten sam klucz co API>" --project Worker/DataAcquisition.Worker.csproj`

Klucza nie wpisywać do `appsettings*.json`, `launchSettings.json` ani repozytorium.

## Wymagania wstępne i dowody przed wdrożeniem

- [ ] Dowody z `verification.md` zatwierdzone.
- [ ] Kopia zapasowa — **nie dotyczy** (brak trwałych danych).
- [ ] API wdrożone w wersji z tego tematu i osiągalne z hosta workera (`curl <BaseUrl>/health` → `Healthy`).
- [ ] `DataAcquisition__Api__ApiKey` workera = klucz API; `DataAcquisition__Api__BaseUrl` ustawiony.

## Przebieg wdrożenia i upoważnienie

- **Autoryzacja produkcji (release manager)**: <osoba, data> — inna osoba niż autor zmiany; agent AI nie autoryzuje wdrożeń.
- **Wykonujący wdrożenie**: <osoba / pipeline>
- **Data i okno serwisowe**: <data, godzina>
- **Kroki wdrożenia**: <do uzupełnienia — sposób uruchamiania nieustalony>. Minimalnie: publish API → start API → `/health` → publish workera → ustawienie zmiennych → start workera → w logu workera `Sent observations … accepted`.

## Procedura wycofania (Rollback)

- Worker: przywrócić poprzednie binaria (odbiorca logujący z DA-002) — dane przestaną trafiać do API, worker działa dalej.
- API: poprzednia wersja przyjmuje paczki bez `units`/`nativeCode`; nowy worker wysyłający te pola do starego API — pola zostaną zignorowane (nie są zapisywane).
- Brak migracji i trwałych danych.
- Kryterium wycofania: w logu workera stale `Error` „API rejected the API key” mimo poprawnej konfiguracji albo `Error` „rejected … as invalid” (paczki pomijane).

## Obserwacja powdrożeniowa (Monitoring)

- Worker `Information` „Sent observations A-B of {Device} …: N accepted, M duplicates” — ciągłość zakresów i `duplicates` ≈ 0 w normalnej pracy (duplikaty tylko po ponowieniach).
- Worker `Error` „API rejected … as invalid; batch skipped” — **utrata danych paczki**; nie powinno występować.
- Worker `Error` „API rejected the API key” — błąd konfiguracji klucza; dane czekają w buforze agenta (~22 min).
- Worker `Warning` „… cycle failed (…)” — niedostępność agenta **lub API** (komunikat ma prefiks agenta — patrz `verification.md`, *Pominięcia*).
- Po okresie obserwacji: status tematu `observed`, następnie `closed`.
