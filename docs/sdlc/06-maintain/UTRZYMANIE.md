# Utrzymanie Systemu i Zarządzanie Incydentami

Zasady monitorowania zdrowia aplikacji na produkcji, procedury awaryjne oraz powrót do planowania (Post-Mortem).

---

## 1. Monitorowanie i kluczowe wskaźniki (SLI / SLO)

| Obszar / Sygnał | Co sprawdzamy | Dopuszczalna norma | Reakcja w razie przekroczenia |
|---|---|---|---|
| **Dostępność API** | Kody HTTP 5xx, czas odpowiedzi (p95) | Dostępność > 99.9%, czas < 500ms | Analiza logów błędów, restart instancji |
| **Kolejki zadań** | Liczba zadań oczekujących, zadania błędne (dead-letter) | Kolejka opróżniana na bieżąco | Sprawdzenie stanu konsumentów |
| **Zasoby serwera** | Zużycie CPU, pamięć RAM, wolne miejsce na dysku | RAM < 85%, CPU < 80% | Skalowanie instancji, czyszczenie logów |
| **Baza danych** | Aktywne połączenia, długie blokady (locks) | Brak transakcji trwających > 30s | Identyfikacja i zabicie blokującej sesji |

---

## 2. Postępowanie w przypadku awarii (Incydent)

1. **Opanowanie sytuacji (Mitigation)**: Priorytetem jest natychmiastowe przywrócenie działania usługi dla użytkowników (np. restart, wycofanie ostatniego wydania, przełączenie na zapasowe łącze).
2. **Rejestracja incydentu**: Wypełnij kartę `docs/sdlc/szablony/incident.md` i zapisz ją w `docs/sdlc/tematy/INC-<ID>-<nazwa>/incident.md`.
3. **Analiza przyczyny (Post-Mortem)**: Ustal faktyczną przyczynę źródłową (Root Cause).
4. **Działania prewencyjne**: Załóż nowy temat (`tools/New-Topic.ps1`) z `intent.md` opisującym trwałą poprawkę (`Tryb: fix` w planie) oraz test regresyjny, aby problem nigdy więcej nie powrócił. Wnioski, które dotyczą pracy agenta AI, dopisz do sekcji *Powtarzające się błędy* w `CLAUDE.md`.
