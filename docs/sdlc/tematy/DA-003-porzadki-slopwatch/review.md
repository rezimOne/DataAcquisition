# Review: DA-003 — Porzadki po slopwatch (SW003, SW004)

Status: not-run. Commit: <SHA>. Baza: <merge-base>. Recenzent: sdlc-reviewer. Data: 2026-11-04.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review DA-003` lub `tools/Invoke-Review.ps1 -Id DA-003`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|
| R-01 | P1 / P2 / P3 | `src/...:<linia>` | <sekwencja zdarzeń prowadząca do problemu i realny skutek> | otwarta / naprawiona / odrzucona |

## Zastosowane skille

| Skill | Dla plików / obszaru | Uwagi |
|---|---|---|
| `<plugin:skill>` | `<ścieżki>` | <załadowany / niedostępny — powód> |

## Zgodność z planem

<Czy każdy zmieniony plik spoza docs/ jest w plan.md; odstępstwa i czy zostały opisane.>

## Pokrycie kryteriów akceptacji

| AC | Test / dowód w verification.md | Ocena recenzenta |
|---|---|---|
| AC-01 | <test> | <pokryte / brak testu / dowód niewiarygodny> |

## Dowody weryfikacji

<Czy verification.md zawiera rzeczywiste wyniki dla przejrzanego kodu; pominięcia i ich zasadność.>

## Historia iteracji

| Iteracja | Commit | Data | Wynik |
|---|---|---|---|
| 1 | <SHA> | 2026-11-04 | <changes-requested: P1 0, P2 1, P3 2> |
