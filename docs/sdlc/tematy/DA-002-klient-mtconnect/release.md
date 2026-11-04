# Wydanie: DA-002

Status: not-released. Commit: <commit po review — uzupełnia release manager>. Wersja paczki / tag: <wersja — nadaje release manager>. Środowisko docelowe: <staging / prod — do ustalenia>.

## Zawartość wydania

- **Zmieniona usługa** `DataAcquisition.Worker`: zamiast szkieletu łączy się z agentem MTConnect (`/probe`, `/current`, `/sample`) i przekazuje obserwacje do odbiorcy, który w DA-002 tylko loguje statystyki paczek. **Nie wysyła jeszcze danych do API** (DA-003).
- **Nowa zależność**: pakiet `Microsoft.Extensions.Http` 10.0.12.
- **API (`DataAcquisition.Api`) i `Contracts/`** — bez zmian.
- **Baza danych / migracje** — brak.

Budowanie paczki: `dotnet publish Worker/DataAcquisition.Worker.csproj -c Release -o publish/worker` (katalog `publish/` jest w `.gitignore`).

## Wymagana konfiguracja

Wszystkie klucze są opcjonalne — wartości domyślne są w kodzie (`MTConnectOptions`) i odpowiadają **lokalnemu symulatorowi**. Na serwerze **trzeba ustawić co najmniej `AgentUrl`**, inaczej worker będzie łączył się z `http://localhost:5000` i logował ostrzeżenia.

| Zmienna środowiskowa | Klucz konfiguracji | Domyślnie | Zakres / opis |
|---|---|---|---|
| `DataAcquisition__MTConnect__AgentUrl` | `DataAcquisition:MTConnect:AgentUrl` | `http://localhost:5000` | Adres agenta MTConnect (absolutny `http`/`https`; może zawierać ścieżkę, np. `http://host:5000/agent`) |
| `DataAcquisition__MTConnect__PollInterval` | `DataAcquisition:MTConnect:PollInterval` | `00:00:01` | 100 ms – 1 h; odstęp między `/sample`, gdy agent nie ma zaległych danych |
| `DataAcquisition__MTConnect__SampleCount` | `DataAcquisition:MTConnect:SampleCount` | `1000` | 1 – 1000; obserwacji na żądanie (= limit paczki API z DA-001) |
| `DataAcquisition__MTConnect__RequestTimeout` | `DataAcquisition:MTConnect:RequestTimeout` | `00:00:10` | 1 s – 5 min |
| `DataAcquisition__MTConnect__RetryInitialDelay` | `DataAcquisition:MTConnect:RetryInitialDelay` | `00:00:01` | 100 ms – `RetryMaxDelay`; pierwsze opóźnienie ponowienia |
| `DataAcquisition__MTConnect__RetryMaxDelay` | `DataAcquisition:MTConnect:RetryMaxDelay` | `00:00:30` | ≤ 1 h; górny limit opóźnienia |

Błędna wartość zatrzymuje start z `OptionsValidationException` wskazującym pełny klucz (np. `DataAcquisition:MTConnect:SampleCount must be between 1 and 1000.`). Agent nie wymaga uwierzytelnienia — brak sekretów w tej zmianie.

## Wymagania wstępne i dowody przed wdrożeniem

- [ ] Dowody z `verification.md` zatwierdzone.
- [ ] Kopia zapasowa bazy danych / stanu — **nie dotyczy** (worker nie przechowuje stanu; po restarcie startuje od `/current`).
- [ ] `DataAcquisition__MTConnect__AgentUrl` ustawiony na adres agenta w środowisku docelowym i agent osiągalny z hosta workera (`curl <AgentUrl>/probe`).
- [ ] Agent mówi MTConnect **1.3** (namespace `urn:mtconnect.org:MTConnectStreams:1.3`) — inne wersje są odrzucane (`Warning` z błędem formatu).

## Przebieg wdrożenia i upoważnienie

- **Autoryzacja produkcji (release manager)**: <osoba, data> — inna osoba niż autor zmiany; agent AI nie autoryzuje wdrożeń.
- **Wykonujący wdrożenie**: <osoba / pipeline>
- **Data i okno serwisowe**: <data, godzina>
- **Kroki wdrożenia**: <do uzupełnienia — sposób uruchamiania (usługa Windows / systemd / kontener) nie jest jeszcze ustalony>. Minimalnie: publish → ustawienie `AgentUrl` → start → w logu `MTConnect agent … N device(s)` i `Synchronized with MTConnect agent instance …`, potem cykliczne `Received … observations`.

## Procedura wycofania (Rollback)

- Przywrócić poprzednie binaria workera (szkielet z DA-001) albo zatrzymać worker — API i agent nie zależą od workera.
- Brak migracji i trwałych danych.
- Kryterium wycofania: worker nie startuje mimo poprawnej konfiguracji albo w logu stale `Warning` z błędem formatu/połączenia mimo osiągalnego agenta.

## Obserwacja powdrożeniowa (Monitoring)

- `Information` „Received {N} observations from {Device} (instance …, Sample), sequence A-B” — ciągłość zakresów (A = poprzednie B + 1) i tempo (~liczba obserwacji/s agenta).
- `Information` „MTConnect agent restarted (instance X -> Y)” — restarty agenta.
- `Warning` „Observations A-B of instance … left the agent buffer … are lost” — worker nie nadąża lub był niedostępny dłużej niż bufor agenta (symulator: ~22 min); powinno nie występować.
- `Warning` „… cycle failed (…); retrying in …” — niedostępność agenta; opóźnienia rosną do 30 s.
- Po okresie obserwacji: status tematu `observed`, następnie `closed` (w `intent.md` i `BACKLOG.md`).
