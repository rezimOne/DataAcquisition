# Instrukcje dla Agentów AI (AGENTS.md)

Ten projekt korzysta ze sformalizowanego procesu SDLC opartego na dokumentach (`docs/sdlc/`). Pełne zasady: [CLAUDE.md](CLAUDE.md).

---

## Wytyczne operacyjne

1. **Przed przystąpieniem do zadania**:
   - Przeczytaj [PROCES.md](docs/sdlc/PROCES.md).
   - Otwórz właściwy temat w `docs/sdlc/tematy/<ID>/` i przeczytaj `intent.md`, `spec.md` i `plan.md`.
   - Jeśli `plan.md` nie ma statusu `approved` — przygotuj lub popraw plan i zatrzymaj się. Status `approved` nadaje wyłącznie człowiek.
   - Upewnij się, że jesteś na gałęzi `topic/<ID>-<nazwa>` — nie commituj na `main`/`master`.

2. **W trakcie prac**:
   - Postępuj zgodnie z krokami w `plan.md`.
   - Twórz testy weryfikujące kryteria akceptacji `AC-xx`.
   - Modyfikuj wyłącznie pliki wskazane w planie; odstępstwa opisz w planie w tym samym commicie.
   - W trybie `fix` nie zmieniaj zacommitowanego testu reprodukującego błąd.

3. **Po zakończeniu implementacji**:
   - Uruchom testy i uzupełnij `verification.md` faktycznymi dowodami (z `Commit:` sprawdzanego kodu).
   - Przygotuj lokalny PR: `/sdlc-pr <ID>` (`pr.md`), potem uruchom review: `/sdlc-review <ID>` (`review.md` pisze niezależny recenzent).
   - Popraw uwagi P1/P2 z `review.md`, zacommituj i powtórz review.
   - Nie edytuj `review.md`, nie zatwierdzaj `pr.md`, nie pushuj i nie merguj — to robi człowiek.
