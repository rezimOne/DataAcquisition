---
name: sdlc-pr
description: Przygotowuje lokalny Pull Request tematu SDLC w docs/sdlc/tematy/<ID>/pr.md - odświeża dane automatyczne (commity, pliki vs plan, AC vs verification) i uzupełnia opis dla recenzenta. Używaj po implementacji i uzupełnieniu verification.md, przed /sdlc-review. Argument - ID tematu (opcjonalny).
---

# /sdlc-pr — lokalny Pull Request tematu

Argument: `$ARGUMENTS` (ID tematu; puste = temat z bieżącej gałęzi `topic/<ID>-<nazwa>`).

1. Upewnij się, że praca jest zacommitowana na gałęzi tematu (`git status`). Jeśli nie — zapytaj użytkownika, czy zacommitować; nie commituj na `main`/`master`.

2. Wygeneruj dane automatyczne:

   ```bash
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/New-PullRequest.ps1 $ARGUMENTS
   ```

3. Przeczytaj w katalogu tematu `intent.md`, `plan.md`, `verification.md` i `pr.md` (zwłaszcza tabelę *Zmienione pliki* i *Kryteria akceptacji*).

4. Uzupełnij w `pr.md` wyłącznie sekcje opisowe: *Opis zmiany*, *Najważniejsze zmiany*, *Ryzyka i wpływ*, *Jak testowano*, *Uwagi dla recenzenta*. Pisz konkretnie, na podstawie faktów z diffu i verification.md.
   - Nie edytuj sekcji między `<!-- auto:start -->` a `<!-- auto:end -->`.
   - Nie zmieniaj `Status:` ani sekcji *Decyzja* — akceptację wpisuje człowiek.

5. Jeśli tabela *Zmienione pliki* zawiera pliki **NIE** (spoza planu) albo AC ma „brak wiersza” w verification — zgłoś to użytkownikowi jako blokery do usunięcia przed review.

6. Zacommituj `pr.md` na gałęzi tematu i zaproponuj następny krok: `/sdlc-review <ID>`.
