# Zasady Przeglądu Zmian (Code Review)

Podczas przeglądu Pull Requesta lub diffa zapoznaj się z plikami `intent.md`, `spec.md`, `plan.md`, `verification.md` i `pr.md` danego tematu.

---

## 5 Filarów Przeglądu Kodu

### 1. Zachowanie i Logika Biznesowa
- Czy zmiana ściśle realizuje kryteria odbioru `AC-xx` z `intent.md`?
- Czy obsłużono scenariusze brzegowe (puste dane, brak sieci, timeouty, wielokrotne kliknięcie / współbieżność)?
- Czy nie wprowadzono niepożądanych efektów ubocznych w innych modułach?

### 2. Dane i Integracje
- Czy zapytania do bazy danych są w 100% parametryzowane (brak ryzyka SQL Injection)?
- Czy migracje bazy danych są odwracalne (posiadają plan rollbacku)?
- Czy operacje na powiązanych rekordach są objęte transakcją?

### 3. Bezpieczeństwo i Dostęp
- Czy autoryzacja jest sprawdzana po stronie serwera / API, a nie tylko ukryciem kontrolki w UI?
- Czy w kodzie, logach, testach lub diffie nie ma haseł, sekretów, tokenów ani danych osobowych?

### 4. Weryfikacja i Dowody Jakości
- Czy w `verification.md` znajdują się **rzeczywiste dowody** z uruchomienia testów dla tego konkretnego commita?
- Czy wszystkie pominięte testy zostały jawnie wyjaśnione w sekcji ograniczeń?
- Czy nowe testy sprawdzają rzeczywiste zachowanie, a nie tylko płytkie wywołanie bez asercji?

### 5. Operacje i Utrzymanie
- Czy zmiana nie wprowadza degradacji wydajności (np. problem N+1 zapytań)?
- Czy błędy są logowane w sposób umożliwiający szybką diagnozę na produkcji?
- Czy `plan.md` (sekcja *Wydanie i odtworzenie*) opisuje nowe zmienne środowiskowe, migracje i punkt powrotu? (`release.md` powstaje dopiero po merge.)

---

## Format zgłaszania uwag w review

Każda uwaga powinna zawierać:
- **Priorytet**:
  - `P1` (Blokada) — Luka bezpieczeństwa, utrata danych, błąd uniemożliwiający działanie.
  - `P2` (Wymagana poprawka) — Niezgodność z AC, brak obsługi błędu, brak testu.
  - `P3` (Sugestia) — Czytelność, drobna optymalizacja, konwencja nazewnicza.
- **Lokalizacja**: Ścieżka do pliku i numer linii.
- **Scenariusz i uzasadnienie**: Jaka sekwencja zdarzeń wywoła problem i jaki będzie realny skutek.

---

## Czego nie zgłaszamy

- Formatowania i stylu, które obsługuje formatter / linter (`.claude/hooks/format.json`, linter projektu).
- Kodu spoza diffa, chyba że zmiana realnie go psuje.
- Preferencji bez skutku (inna nazwa zmiennej, inna, równoważna konstrukcja języka).
- Hipotez bez scenariusza — jeśli nie da się opisać sekwencji zdarzeń prowadzącej do błędu, to nie jest uwaga.

---

## Rola AI w review

- **Lokalnie (domyślnie)**: `/sdlc-review <ID>` w sesji Claude Code lub `tools/Invoke-Review.ps1 -Id <ID>` bez sesji. Przegląd wykonuje osobny subagent `sdlc-reviewer` — świeży kontekst, bez powłoki, zapis wyłącznie do `docs/sdlc/tematy/<ID>/review.md` (pilnuje hook). Autor zmian nie może edytować `review.md`. Recenzent ładuje skille z tabeli routingu w `CLAUDE.md` i z sekcji *Wymagane skille* w `plan.md` (narzędzie Skill) i ocenia zmianę także względem nich — lista trafia do sekcji *Zastosowane skille* w `review.md`.
- Uwagi są **doradcze**. Decyzję podejmuje człowiek w `pr.md` (`Status: approved`), a push przepuszcza dopiero `tools/Test-TopicReady.ps1` (hook git `pre-push`): review musi dotyczyć aktualnego kodu i nie mieć otwartych P1/P2. Agent, który napisał kod, nie może go zatwierdzić; merge wykonuje człowiek na zdalnym repozytorium.
- Poprawki po review: autor poprawia kod, commituje i uruchamia review ponownie — recenzent zmienia status uwag na `naprawiona`. Uwagę, której nie poprawiamy, człowiek oznacza jako `odrzucona` z uzasadnieniem.
- **Opcjonalnie w CI** (`Init-SDLC.ps1 -IncludeCI`): `.github/workflows/claude-review.yml` (review PR) i `claude.yml` (poprawki przez `@claude`).
- Uwaga, która powtarza się w kolejnych tematach, trafia do sekcji *Powtarzające się błędy* w `CLAUDE.md`, a jeśli da się ją sprawdzić deterministycznie — do hooka lub bramek `Test-TopicReady`.
