# Wydanie: DA-002

Status: not-released. Commit: 417e369. Wersja paczki / tag: <wersja>. Środowisko docelowe: <staging / prod>.

## Zawartość wydania

<Lista komponentów (backend, frontend, worker, migracje bazy danych, pliki konfiguracyjne), manifest i sumy kontrolne.>

## Wymagania wstępne i dowody przed wdrożeniem

- [ ] Dowody z `verification.md` zatwierdzone.
- [ ] Kopia zapasowa bazy danych / stanu wykonana.
- [ ] Zmienne środowiskowe i klucze skonfigurowane w środowisku docelowym.

## Przebieg wdrożenia i upoważnienie

- **Autoryzacja produkcji (release manager)**: <osoba, data> — inna osoba niż autor zmiany; agent AI nie autoryzuje wdrożeń.
- **Wykonujący wdrożenie**: <osoba / pipeline>
- **Data i okno serwisowe**: <data, godzina>
- **Kroki wdrożenia**: <komendy, skrypty, kolejność uruchamiania>

## Procedura wycofania (Rollback)

<Konkretne instrukcje awaryjne: jak cofnąć binaria, jak cofnąć migrację bazy lub przywrócić snapshot, kryteria podjęcia decyzji o wycofaniu.>

## Obserwacja powdrożeniowa (Monitoring)

<Kluczowe metryki, logi, zachowanie kolejki i czas obserwacji przed ostatecznym zamknięciem tematu. Po okresie obserwacji: status tematu `observed`, następnie `closed` (w `intent.md` i `BACKLOG.md`).>
