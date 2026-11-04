---
name: sdlc-review
description: Lokalne AI review gałęzi tematu SDLC - przygotowuje diff i uruchamia niezależnego recenzenta (subagent sdlc-reviewer), który zapisuje docs/sdlc/tematy/<ID>/review.md. Używaj po /sdlc-pr, przed akceptacją PR przez człowieka. Argument - ID tematu (opcjonalny; domyślnie z gałęzi topic/<ID>-<nazwa>).
---

# /sdlc-review — lokalne review tematu

Argument: `$ARGUMENTS` (ID tematu, np. `PROJ-001`; puste = temat z bieżącej gałęzi `topic/<ID>-<nazwa>`).

1. Odśwież dane PR i diff dla recenzenta:

   ```bash
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/New-PullRequest.ps1 $ARGUMENTS
   ```

   Jeśli skrypt zgłosi błąd (brak gałęzi tematu, brak `pr.md`) — przekaż go użytkownikowi i zatrzymaj się. Review obejmuje tylko zacommitowany kod: jeśli `git status` pokazuje niezacommitowane zmiany poza dokumentami tematu, poproś o commit przed review.

2. Uruchom subagenta **`sdlc-reviewer`** (narzędzie Agent) z poleceniem: „Przeprowadź review tematu <ID> i zapisz review.md”. Nie przeprowadzaj review samodzielnie i nie edytuj `review.md` — zapis jest dozwolony wyłącznie dla recenzenta.

3. Po zakończeniu przeczytaj `docs/sdlc/tematy/<ID>-<nazwa>/review.md` i pokaż użytkownikowi: status, przejrzany commit, otwarte uwagi P1/P2 (ID, plik:linia, jedno zdanie), liczbę P3.

4. Zakończ informacją o następnym kroku:
   - są otwarte P1/P2 → „popraw uwagi, zacommituj i uruchom /sdlc-review ponownie”;
   - brak otwartych P1/P2 → „przeczytaj review.md; akceptację wpisuje człowiek w pr.md (Status: approved), potem `tools/Test-TopicReady.ps1` i `git push`”.

Nie zmieniaj statusów w `pr.md`, nie pushuj i nie merguj.
