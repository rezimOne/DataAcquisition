# Spec: <ID — tytuł>

Status: draft. Odpowiedzialny: <osoba>. Data: <data>. Źródło: [intent](intent.md).

## Zachowanie

<Scenariusz poprawny (happy path), błędy, zachowanie częściowe, timeouty, ponowienia i wyścigi (concurrency).>

## Dane i kontrakty

<Pola, formaty, jednostki, strefa czasowa (UTC/lokalny), relacje i krotność, wersje API / schematy SQL. Puste/brak danych nie oznacza zera.>

## Dostęp i interfejsy

<Role, uprawnienia serwerowe, walidacja wejścia, komunikaty dla użytkownika, logowanie bez wycieku sekretów.>

## Kryteria odbioru

| AC | Przypadek testowy / Wejście | Oczekiwany wynik |
|---|---|---|
| AC-01 | <dokładne wejście / krok> | <oczekiwany stan / kod HTTP / zapis w bazie> |
| AC-02 | <scenariusz błędu / niepoprawne dane> | <oczekiwana obsługa błędu> |

## Ryzyka i rozstrzygnięcia

<Kompatybilność wsteczna, migracja danych, ryzyko awarii, alternatywy. W razie dużej zmiany link do ADR, np. `../../02-design/ADR-001-<tytul>.md`.>
