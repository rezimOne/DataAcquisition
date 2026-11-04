# Przygotowanie i Odbiór Wydania

Zasady publikacji, zarządzania wersjami, wdrażania migracji bazodanowych i procedury awaryjnego wycofania.

---

## 1. Wersjonowanie i Artefakty

- Projekt stosuje Semantic Versioning (`MAJOR.MINOR.PATCH`).
- Każde wydanie produkcyjne jest powiązane z konkretnym tagiem w Git oraz manifestem wersji.
- Paczki wdrożeniowe budowane są w sposób deterministyczny (powtarzalny).

---

## 2. Checklista przedwdrożeniowa (Pre-flight checklist)

- [ ] Wszystkie testy automatyczne przeszły pomyślnie na gałęzi wydania.
- [ ] Dokument `verification.md` został zaakceptowany przez osobę weryfikującą.
- [ ] Skrypty migracji bazy danych zostały przetestowane w obie strony (Upgrade oraz Downgrade/Rollback).
- [ ] Wykonano kopię zapasową bazy danych produkcyjnej przed startem okna serwisowego.
- [ ] Przygotowano plan wycofania (Rollback plan).

---

## 3. Postępowanie w razie niepowodzenia (Rollback)

Nigdy nie zakładaj, że cofnięcie plików aplikacji wystarczy, jeśli wykonano migrację bazy danych. W karcie wydania (`release.md`) należy zawsze zdefiniować:
1. Jak bezpiecznie zatrzymać ruch użytkowników.
2. Jak przywrócić stan bazy (odwrócenie migracji lub odtworzenie ze snapshotu).
3. Jak przywrócić poprzednią wersję binariów/kontenera.
4. Kryterium czasu (np. jeśli wdrożenie nie powiedzie się w ciągu 20 minut, automatycznie uruchamiamy procedurę wycofania).
