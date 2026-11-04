# Rejestr Tematów i Backlog Projektu

Wszystkie planowane, trwające i zakończone prace rozwojowe są ewidencjonowane poniżej.

---

## Tabela tematów

| ID | Status | Priorytet | Skala | Tytuł i link do intencji | Odpowiedzialny | Warunek / Zależność |
|---|---|---|---|---|---|---|
| [DA-001](../tematy/DA-001-api-odbioru-danych/intent.md) | verified | P2 | M | API odbioru danych z maszyn | nog_programista2 | - |
| [DA-002](../tematy/DA-002-klient-mtconnect/intent.md) | ready | P2 | M | Klient MTConnect w workerze | nog_programista2 | - |

---

## Oznaczenia i legendy

- **Priorytet**:
  - `P1` — Krytyczny / błąd produkcyjny / blokada biznesowa.
  - `P2` — Ważna funkcja w bieżącym etapie rozwoju.
  - `P3` — Usprawnienie, optymalizacja, dług techniczny.
- **Skala (T-shirt size)**:
  - `S` — Mała zmiana, 1-2 pliki, proste testy jednostkowe.
  - `M` — Kilka komponentów, integracja z bazą, testy integracyjne.
  - `L` — Duży moduł, nowa integracja zewnętrzna, migracja danych.
- **Statusy**:
  - `draft` → `ready` → `in-progress` → `verified` → `released` → `observed` → `closed`.
  - Wyjątkowe: `blocked`, `rejected`, `superseded`.
