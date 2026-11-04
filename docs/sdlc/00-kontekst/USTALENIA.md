# Ustalenia Techniczne i Rejestr Ryzyk

Rejestr fundamentalnych ustaleń technicznych, decyzji o granicach kompromisów i zidentyfikowanych ryzyk (Findings).

---

## Rejestr ryzyk i ustaleń technicznych

| ID | Obszar | Opis ustalenia / ryzyka | Wpływ / Konsekwencja | Rekomendowane działanie |
|---|---|---|---|---|
| **F-01** | Baza danych / SQL | Zapytania parametryzowane są obowiązkowe | Ryzyko SQL Injection i słabego cache planu | Zakaz konkatenacji stringów w zapytaniach SQL |
| **F-02** | Bezpieczeństwo | Hasła i dane wrażliwe w logach | Wyciek danych w razie dostępu do logów | Maskowanie pól wrażliwych na poziomie loggera |
| **F-03** | Współbieżność | Równoczesna edycja tego samego rekordu | Nadpisywanie cudzych zmian (Lost Update) | Blokada optymistyczna (wersja / rowversion) |
| **F-04** | Integracje | Zewnętrzne API może być niedostępne | Blokowanie wątków i zawieszenie aplikacji | Circuit breaker, timeouty i kolejkowanie offline |

---

## Jak zgłaszać nowe ustalenie

Jeśli podczas analizy lub prac programistycznych natrafisz na istotne ryzyko techniczne:
1. Dodaj wpis do powyższej tabeli z unikalnym ID (`F-XX`).
2. Podaj konkretny plik, scenariusz wyzwalający oraz realny skutek.
3. Powiąż z odpowiednim tematem w `01-plan/BACKLOG.md`.
