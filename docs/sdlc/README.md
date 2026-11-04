# Dokumentacja i prowadzenie rozwoju (SDLC)

Niniejszy katalog zawiera dokumentację techniczną projektu, rejestr decyzji architektonicznych oraz pełną historię i szablony realizacji kolejnych tematów rozwojowych.

---

## 🗺️ Mapa dokumentacji

1. **[Proces SDLC](PROCES.md)** — jak prowadzić zadania od intencji po wydanie.
2. **[Kontekst i Architektura](00-kontekst/ANALIZA-PROJEKTU.md)** — ogólny opis systemu, technologie i ograniczenia.
3. **[Stan Funkcji i Modułów](00-kontekst/STAN-FUNKCJI.md)** — spis modułów oraz ich dojrzałość.
4. **[Ustalenia i Ryzyka](00-kontekst/USTALENIA.md)** — rejestr ryzyk technicznych i reguł krytycznych.
5. **[Backlog Projektu](01-plan/BACKLOG.md)** — priorytetyzowany rejestr planowanych tematów.
6. **[Rejestr Decyzji (ADR)](02-design/DECYZJE.md)** — kluczowe rozstrzygnięcia architektoniczne.
7. **[Praca z Kodem](03-build/PRACA-Z-KODEM.md)** — środowisko deweloperskie, standardy, komendy build/lint.
8. **[Strategia Testów](04-test/STRATEGIA.md)** — poziomy weryfikacji i wymagane dowody.
9. **[Wydania i Wdrożenia](05-deploy/WYDANIE.md)** — procedury wydań, migracje bazy i rollback.
10. **[Utrzymanie i Monitorowanie](06-maintain/UTRZYMANIE.md)** — obserwacja działania, reagowanie na awarie.

---

## 📁 Układ katalogów

```text
docs/sdlc/
  README.md, PROCES.md          # Główne wejście i reguły procesu
  00-kontekst/                  # Stała wiedza o systemie i modułach
    moduly/                     # Opisy poszczególnych komponentów systemu
  01-plan/BACKLOG.md            # Spis tematów i priorytety
  02-design/DECYZJE.md          # Rejestr decyzji i linki do ADR
  03-build/PRACA-Z-KODEM.md      # Instrukcje dla programistów i komendy
  04-test/STRATEGIA.md          # Scenariusze i dowody testowe
  05-deploy/WYDANIE.md          # Standard wydań
  06-maintain/UTRZYMANIE.md      # Utrzymanie i postępowanie poincydentalne
  tematy/                       # Karty poszczególnych zmian (np. PROJ-001-...)
  szablony/                     # Szablony dokumentów (temat, adr, incident)
```

> [!NOTE]
> Wszystkie dokumenty powiązane z pojedynczą zmianą żyją razem w katalogu `tematy/<ID>-<nazwa>/`, dzięki czemu intencja, specyfikacja, plan wdrożenia i dowody testów są zarchiwizowane w jednym miejscu w historii repozytorium.
