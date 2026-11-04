# Review: <ID — tytuł>

Status: not-run. Commit: <SHA>. Baza: <baza>. Recenzent: sdlc-reviewer. Data: <data>.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review <ID>` lub `tools/Invoke-Review.ps1 -Id <ID>`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|
| R-01 | P1 / P2 / P3 | `src/...:<linia>` | <sekwencja zdarzeń prowadząca do problemu i realny skutek> | otwarta / naprawiona / odrzucona |

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
| 1 | <SHA> | <data> | <changes-requested: P1 0, P2 1, P3 2> |
