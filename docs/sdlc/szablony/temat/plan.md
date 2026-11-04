# Plan: <ID — tytuł>

Status: draft. Tryb: feature. Baza: <commit>. Odpowiedzialny: <osoba>. Źródło: [spec](spec.md).

> Cykl statusu: draft → approved → done. Status approved nadaje wyłącznie człowiek (hook blokuje agenta). Tylko przy statusie approved hook pozwala edytować pliki wymienione w tabeli poniżej — ścieżki względem korzenia repozytorium, w backtickach; dozwolony katalog zakończony ukośnikiem lub wzorzec z gwiazdką (nie może zaczynać się od gwiazdki).
> Tryb fix — testy zacommitowane w HEAD są zablokowane do edycji: najpierw commit z czerwonym testem, potem poprawka bez zmiany testu.

## Pliki i kolejność zmian

| Krok | Plik / Komponent | Rodzaj zmiany | Opis | Zależność |
|---|---|---|---|---|
| 1 | `tests/...` | [NEW] | Test jednostkowy / regresyjny wykazujący brak lub potrzebę zmiany | <brak> |
| 2 | `src/...` | [MODIFY] | Implementacja właściwej logiki biznesowej | Krok 1 |
| 3 | `src/...` | [MODIFY] | Zmiana kontraktu / konfiguracji / UI | Krok 2 |

## Ryzyka

<Co może pójść źle w implementacji (regresje, współbieżność, wydajność, kompatybilność) i jak to wykryjemy.>

## Weryfikacja

<Konkretne komendy testowe i scenariusze weryfikacji powiązane z AC. Wymagania środowiskowe (baza, mocki, klucze — bez wartości sekretów).>

## Wydanie i odtworzenie (Rollback)

<Migracje bazodanowe, nowe zmienne środowiskowe, manifest, punkt powrotu w razie niepowodzenia.>

## Zgoda na zakres i odstępstwa

- **Akceptacja planu**: <osoba, rola, data> — wpisuje człowiek. Nie dopisuj zgody bez faktycznego potwierdzenia.
- **Odstępstwa**: <zmiany planu w trakcie implementacji wraz z uzasadnieniem; aktualizuj w tym samym commicie co kod, którego dotyczą>.
