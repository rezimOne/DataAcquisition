# Intent: DA-001 — API odbioru danych z maszyn

ID: DA-001. Status: verified. Data: 2026-11-04. Odpowiedzialny: nog_programista2. Baza: 495a418.

## Problem i dowód

**Fakt**: Worker (`Worker/Worker.cs`) jest szkieletem z szablonu `dotnet new worker` — tylko loguje co 1 s. W solucji (`DataAcquisition.slnx`) nie ma żadnego API, więc dane z maszyny CNC (agent MTConnect, symulator `resources/cnc_mtconnect_sim.py`) nie mają dokąd trafić. Architektura (`docs/sdlc/00-kontekst/ANALIZA-PROJEKTU.md`) zakłada przepływ: agent MTConnect → Worker → API.

**Hipoteza**: Zdefiniowanie kontraktu i postawienie API jako pierwszego kroku pozwoli kolejnym tematom (klient MTConnect, wysyłka z workera) pracować na stabilnym, przetestowanym kontrakcie.

## Oczekiwany rezultat

W solucji istnieje usługa HTTP `DataAcquisition.Api`, która:
- przyjmuje paczki obserwacji MTConnect od workera,
- odrzuca dane niepoprawne i niezautoryzowane z jednoznacznym kodem błędu,
- pozwala odczytać zapisane obserwacje (do weryfikacji i dalszych integracji),
- ma kontrakt współdzielony z workerem (bez duplikowania modeli DTO).

## Użytkownicy, systemy i ograniczenia

- **Dotknięci użytkownicy / role**: Worker DataAcquisition (klient zapisu); konsumenci danych — monitoring/analityka (klient odczytu, w przyszłości).
- **Dotknięte systemy / integracje**: nowy projekt `DataAcquisition.Api`, nowy projekt kontraktów współdzielonych, nowy projekt testów API, plik solucji. Worker **nie** jest zmieniany.
- **Ograniczenia**: .NET 10, ASP.NET Core Minimal API, xUnit v3; zero sekretów w repozytorium (klucz API przez User Secrets / zmienne środowiskowe); obserwacje zgodne z modelem MTConnect (sekwencja, `instanceId`, `dataItemId`, znacznik czasu, wartość, kategoria).

## Zakres i poza zakresem

- **W zakresie**:
  - projekt `DataAcquisition.Api` (Minimal API) i projekt kontraktów (DTO paczki obserwacji),
  - endpoint zapisu paczki obserwacji z walidacją,
  - endpoint odczytu obserwacji z filtrem (urządzenie, zakres czasu, limit),
  - endpoint health check,
  - uwierzytelnianie workera kluczem API w nagłówku,
  - idempotencja zapisu: ponowne wysłanie tej samej obserwacji (urządzenie + `instanceId` + `sequence`) nie tworzy duplikatu,
  - przechowywanie **w pamięci** za interfejsem repozytorium (wymiana na bazę w osobnym temacie),
  - testy integracyjne API (`WebApplicationFactory`) w nowym projekcie testów.
- **Poza zakresem**:
  - trwała baza danych (osobny temat + ADR wyboru bazy),
  - zmiany w workerze: klient MTConnect, klient API, wysyłka (osobne tematy),
  - użytkownicy, role i OAuth/OIDC; HTTPS/certyfikaty produkcyjne; wdrożenie i konteneryzacja,
  - agregacje, OEE, dashboardy.

## Kryteria sukcesu (Acceptance Criteria)

- AC-01: `dotnet build DataAcquisition.slnx` i `dotnet test DataAcquisition.slnx` kończą się kodem 0; solucja zawiera projekt API, kontraktów i testów API.
- AC-02: Poprawna paczka obserwacji wysłana z prawidłowym kluczem API jest przyjmowana (2xx), a obserwacje są następnie zwracane przez endpoint odczytu.
- AC-03: Żądanie zapisu bez klucza API lub z błędnym kluczem jest odrzucane (401) i nic nie zostaje zapisane.
- AC-04: Paczka niepoprawna (np. pusta, brak `deviceId`/`dataItemId`/`sequence`/`timestamp`, przekroczony limit rozmiaru) jest odrzucana (400) z opisem błędów w formacie `ProblemDetails` i nic z niej nie zostaje zapisane.
- AC-05: Ponowne wysłanie tych samych obserwacji (ten sam `deviceId` + `instanceId` + `sequence`) nie tworzy duplikatów; odpowiedź informuje, ile obserwacji przyjęto, a ile pominięto jako duplikaty.
- AC-06: Endpoint odczytu filtruje po `deviceId` i zakresie czasu, zwraca wyniki posortowane po `sequence` i respektuje limit liczby wyników.
- AC-07: Endpoint health check zwraca 200 bez uwierzytelnienia.
- AC-08: Klucz API nie jest zapisany w repozytorium ani w logach; aplikacja bez skonfigurowanego klucza nie startuje (walidacja Options przy starcie).

## Pytania i decyzja

| Pytanie | Decyzja | Kto, kiedy |
|---|---|---|
| Przechowywanie w pierwszym kroku | W pamięci za interfejsem repozytorium; trwała baza w osobnym temacie z ADR | Product owner, 2026-11-04 |
| Uwierzytelnianie workera | Klucz API w nagłówku `X-Api-Key` | Product owner, 2026-11-04 |
| Czy odczyt ma być chroniony | Tak, tym samym kluczem; osobne role w przyszłym temacie | Product owner, 2026-11-04 |
| Maksymalny rozmiar paczki | 1000 obserwacji na żądanie | Product owner, 2026-11-04 |
| Wartości `UNAVAILABLE` i CONDITION (alarmy) | Przyjmowane jako zwykłe obserwacje: wartość tekstowa + kategoria; dla CONDITION dodatkowo poziom (`Normal`/`Warning`/`Fault`) — do doprecyzowania w `spec.md` | Tech lead (otwarte) |

- **Decyzja product ownera**: zaakceptowano — nog_programista2, 2026-11-04. Status tematu: `ready`.
