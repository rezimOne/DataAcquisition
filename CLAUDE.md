# Instrukcje i Kontekst dla Asystenta AI (CLAUDE / AGENTS)

Projekt: DataAcquisition. Start: zapoznaj się z [dokumentacją SDLC](docs/sdlc/README.md), a następnie z aktywnym tematem w `docs/sdlc/tematy/<ID>/`.

---

## 📌 Zasady pracy w tym repozytorium

1. **Proces Spec-First**:
   - Nie modyfikuj kodu produkcyjnego bez zatwierdzonego `intent.md` oraz `plan.md` w katalogu tematu.
   - Analiza problemu i inspekcja plików nie oznacza zgody na edycję kodu. Zaczynaj w plan mode.
   - Status `approved` w `plan.md` nadaje wyłącznie człowiek. Przygotuj plan, przedstaw go i zatrzymaj się.

2. **Ścisłe trzymanie się planu**:
   - Edytuj tylko te pliki, które zostały wskazane w `plan.md`.
   - Gdy implementacja wymaga zmiany planu, zaktualizuj sekcję *Odstępstwa* w tym samym commicie co kod.
   - Nie wprowadzaj zmian kosmetycznych, formatowania ani refaktoringu w plikach spoza zdefiniowanego zakresu.

3. **Zasada prawdy i dowodu**:
   - Nie deklaruj, że test przeszedł, jeśli nie uruchomiłeś odpowiedniego polecenia i nie otrzymałeś kodu 0.
   - Wypełniając `verification.md`, wklejaj rzeczywisty output z konsoli. Jeśli czegoś nie możesz przetestować, wpisz to jawnie do sekcji *Pominięcia i ograniczenia*.
   - Przy naprawie błędu (`Tryb: fix`): najpierw commit z testem reprodukującym błąd, potem poprawka bez zmiany tego testu.

4. **Gałąź tematu i lokalny Pull Request**:
   - Pracuj wyłącznie na gałęzi `topic/<ID>-<nazwa>` (tworzy ją `tools/New-Topic.ps1`). Nie commituj na `main`/`master`.
   - Po implementacji i `verification.md`: `/sdlc-pr <ID>` (opis PR w `pr.md`), potem `/sdlc-review <ID>` (niezależny recenzent zapisuje `review.md`).
   - Uwagi z `review.md` poprawiaj w kodzie, commituj i uruchamiaj review ponownie. Nie edytuj `review.md` i nie zmieniaj `Status:` ani sekcji *Decyzja* w `pr.md` — to robi recenzent i człowiek.
   - Nie pushuj, nie merguj i nie wdrażaj — push (z bramkami `tools/Test-TopicReady.ps1`) i merge na zdalnym repozytorium wykonuje człowiek.

5. **Bezpieczeństwo**:
   - Nigdy nie zapisuj w kodzie, testach, dokumentacji ani commitach haseł, kluczy prywatnych ani produkcyjnych tokenów.

---

## 🔒 Bramki egzekwowane automatycznie

Reguły powyżej są wspierane przez hooki w `.claude/settings.json` (agent nie może ich edytować):
- edycja plików spoza `docs/` tylko, gdy są wymienione w `plan.md` ze statusem `approved` (także w worktree; plan `done` niczego nie autoryzuje);
- agent nie może nadać statusu `approved` w `plan.md` ani `pr.md`; zmiana zatwierdzonego dokumentu wymaga potwierdzenia człowieka;
- `review.md` zapisuje wyłącznie recenzent `sdlc-reviewer`;
- `.claude/` (settings, hooks, agents, skills, sdlc.json), `.githooks/` i `tools/` zmienia tylko człowiek;
- w `Tryb: fix` zacommitowane testy są zablokowane;
- skan sekretów w każdej zapisywanej treści;
- blokada commitów, merge i rebase na `main`/`master`; blokada `git push` na `main`/`master`, `--force` i `--no-verify`.

Jeśli hook zablokuje operację — nie obchodź go (np. przez `Bash`/`sed`). Zgłoś blokadę człowiekowi.

---

## 🛠️ Komendy deweloperskie

Jedna komenda na czynność — agent musi móc sam zweryfikować swoją pracę. Uruchamiaj z katalogu głównego repozytorium (.NET SDK 10).

```bash
# Budowanie projektu
dotnet build DataAcquisition.slnx

# Uruchamianie testów (xUnit v3)
dotnet test DataAcquisition.slnx

# Lint / formatowanie (weryfikacja, kod 0 = brak zmian do wprowadzenia)
dotnet format DataAcquisition.slnx --verify-no-changes

# Symulator CNC / agent MTConnect (Python 3.9+, http://localhost:5000)
py resources/cnc_mtconnect_sim.py --seed 42

# Uruchomienie workera
dotnet run --project Worker/DataAcquisition.Worker.csproj
```

---

## ⚠️ Powtarzające się błędy

<Uzupełniaj na podstawie uwag z review, które wracają w kolejnych PR. Jedna linia na błąd: co jest źle → jak ma być.>

Przed zatwierdzeniem zmian upewnij się, że kod spełnia kryteria opisane w [REVIEW.md](REVIEW.md).
