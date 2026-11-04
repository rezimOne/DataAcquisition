# Proces prowadzenia zmian i tematów SDLC

Każda zmiana w projekcie (nowa funkcja, refaktor, naprawa błędu) jest traktowana jako **Temat** posiadający własny identyfikator (np. `PROJ-001`), przypisaną osobę odpowiedzialną oraz dedykowany katalog w `docs/sdlc/tematy/<ID>-<nazwa>/`.

---

## 1. Cykl życia tematu

| Etap | Plik wejściowy | Wynik etapu | Warunek przejścia (Gate) | Kto zatwierdza |
|---|---|---|---|---|
| **1. Plan** | Zgłoszenie / potrzeba / incydent | `intent.md` | Jasno zdefiniowany cel, zakres, kryteria sukcesu (AC) i zgoda na realizację. | Product owner |
| **2. Design** | Intencja (`intent.md`) | `spec.md`, opcjonalnie `ADR` | Rozstrzygnięte zachowania brzegowe, kontrakty danych, bezpieczeństwo. | Product owner; tech lead przy zmianach wysokiego ryzyka |
| **3. Build** | Specyfikacja (`spec.md`) | `plan.md` oraz kod/diff | Plan ze statusem `approved` (nadaje człowiek); implementacja w wyznaczonym zakresie. | Inżynier; tech lead / architekt przy wysokim ryzyku |
| **4. Test i Review** | Zmiana w kodzie na gałęzi tematu | `verification.md`, `pr.md`, `review.md` | Rzeczywiste wyniki testów 1:1 z AC; aktualne review bez otwartych P1/P2; bramki `Test-TopicReady` = GO. | Autor / code owner — `Status: approved` w `pr.md`; merge ręcznie na zdalnym repozytorium |
| **5. Deploy** | Zweryfikowany kod | `release.md` | Gotowa paczka, migracje, procedura rollbacku, autoryzacja wdrożenia. | Release manager (inna osoba niż autor) |
| **6. Maintain** | Działanie produkcyjne | `incident.md` lub zamknięcie | Obserwacja metryk; awarie i znaleziska wracają jako nowe tematy z `intent.md`. | Właściciel usługi / dyżurny |

---

## 2. Instrukcja uruchomienia tematu

1. **Utwórz temat i gałąź**:
   `tools/New-Topic.ps1 -Id <ID> -Name <nazwa>` tworzy katalog `docs/sdlc/tematy/<ID>-<nazwa>/` (w tym `pr.md` i `review.md`), wiersz w `01-plan/BACKLOG.md` oraz **gałąź `topic/<ID>-<nazwa>`** od gałęzi bazowej (`baseBranch` w `.claude/sdlc.json`), niezależnie od tego, na jakiej gałęzi jesteś (`-NoBranch` wyłącza). Cała praca nad tematem odbywa się na tej gałęzi — agent ma blokadę commitów na `main`/`master`.
2. **Wypełnij `intent.md`**:
   Określ problem, mierzalny cel, zakres i kryteria akceptacji (`AC-01`, `AC-02`). Product owner akceptuje lub odrzuca intencję.
3. **Wypełnij `spec.md`**:
   Opisz przypadki brzegowe, formaty danych, uprawnienia i błędy. Każde AC z `intent.md` musi mieć wiersz w tabeli kryteriów odbioru. Jeśli zmieniasz architekturę, stwórz ADR na bazie `szablony/adr.md`.
4. **Wypełnij `plan.md`**:
   Wskaż konkretne pliki, testy, ryzyka i kolejność zmian. Ustaw `Tryb: fix` dla napraw błędów. Człowiek zatwierdza plan, zmieniając status na `approved` i wpisując się w sekcji *Zgoda na zakres*.
5. **Implementacja i `verification.md`**:
   Zaimplementuj kod (hook przepuści tylko pliki z planu) i commituj na gałęzi tematu. Uruchom testy i uzupełnij `verification.md` **rzeczywistymi dowodami**; w nagłówku `Commit:` wpisz commit, którego dotyczą dowody.
6. **Lokalny Pull Request — `/sdlc-pr <ID>`**:
   `tools/New-PullRequest.ps1` odświeża w `pr.md` dane automatyczne (commity, pliki z oznaczeniem „w planie?”, AC vs wyniki z `verification.md`) i zapisuje diff dla recenzenta w `.claude/sdlc-tmp/` (poza gitem). Agent uzupełnia opis zmian dla recenzenta.
7. **Lokalne review — `/sdlc-review <ID>`** (w sesji) **lub `tools/Invoke-Review.ps1 -Id <ID>`** (bez sesji, `claude -p`):
   Niezależny subagent `sdlc-reviewer` (świeży kontekst, bez powłoki) zapisuje `review.md`: uwagi P1/P2/P3 ze statusem, zgodność z planem, pokrycie AC, ocena dowodów i przejrzany commit. Autor nie może edytować `review.md`. Pętla: popraw uwagi → commit → review ponownie (uwagi przechodzą na `naprawiona`).
8. **Decyzja człowieka** (jeden commit na gałęzi tematu):
   Po przeczytaniu `review.md` człowiek ustawia w `pr.md` `Status: approved` i wpisuje się w sekcji *Decyzja*, w `plan.md` ustawia `Status: done` (plan przestaje autoryzować edycję plików — po merge nie otworzy ich innym tematom), a temat w `intent.md` i `BACKLOG.md` oznacza jako `verified`. Uwagę, której nie zamierza poprawiać, może oznaczyć w `review.md` jako `odrzucona` z uzasadnieniem. Commit tych dokumentów jest wymagany — bramki sprawdzają czyste drzewo.
9. **Bramki i push — `tools/Test-TopicReady.ps1`**:
   Raport GO/NO-GO: czyste drzewo, plan i zakres plików, plan zamknięty (`done`), `verification` i `review` aktualne dla kodu (po ich commitach zmieniły się tylko dokumenty tematu), brak otwartych P1/P2, `pr.md` approved, zielone testy (`testCommand` z `.claude/sdlc.json`). Ten sam skrypt uruchamia hook git `pre-push` przy wypychaniu gałęzi `topic/*` i blokuje push przy NO-GO. **Merge wykonuje człowiek na zdalnym repozytorium** — nigdy automatycznie.
10. **Wydanie i `release.md`**:
   Release manager autoryzuje wdrożenie i oznacza temat jako `released`. Po okresie obserwacji temat przechodzi na `observed`, a następnie `closed`.

---

## 3. Statusy

**Status tematu** prowadzony jest w nagłówku `intent.md` oraz w `01-plan/BACKLOG.md`:
```text
draft → ready → in-progress → verified → released → observed → closed
```
Statusy wyjątkowe: `blocked` (zablokowany), `rejected` (odrzucony), `superseded` (zastąpiony innym).

| Status tematu | Kiedy | Kto ustawia |
|---|---|---|
| `draft` | utworzenie tematu (`New-Topic.ps1`) | skrypt |
| `ready` | akceptacja `intent.md` | product owner |
| `in-progress` | zatwierdzenie `plan.md` (`approved`) | człowiek zatwierdzający plan (agent może zaktualizować `intent.md`/`BACKLOG.md` na jego polecenie) |
| `verified` | akceptacja `pr.md` (krok 8) | człowiek akceptujący PR |
| `released` | wdrożenie (`release.md`: `released`) | release manager |
| `observed` → `closed` | koniec okresu obserwacji | właściciel usługi |

**Statusy dokumentów** w katalogu tematu:

| Dokument | Statusy |
|---|---|
| `spec.md` | `draft` → `approved` |
| `plan.md` | `draft` → `approved` (wyłącznie człowiek) → `done` (człowiek, przy akceptacji PR; plan przestaje autoryzować edycję plików) |
| `verification.md` | `not-run` → `failed` / `verified` |
| `pr.md` | `draft` → `approved` (wyłącznie człowiek, po przeczytaniu review) |
| `review.md` | `not-run` → `changes-requested` / `no-blocking-findings` (wyłącznie recenzent `sdlc-reviewer`); uwagi: `otwarta` → `naprawiona` / `odrzucona` |
| `release.md` | `not-released` → `released` / `rolled-back` |

> [!IMPORTANT]
> **Zasada prawdy o statusie**: Status nie zmienia się automatycznie tylko dlatego, że asystent AI zakończył generowanie odpowiedzi. Status wymaga faktycznego wykonania i weryfikacji.

---

## 4. Zasady współpracy z AI

- AI jest asystentem przygotowującym propozycje dokumentów, kodu i testów.
- AI **nie ma prawa** zaczynać pisania kodu bez zatwierdzonego `intent.md` i `plan.md`.
- AI **nie ma prawa** deklarować, że testy przeszły pomyślnie, bez faktycznego uruchomienia polecenia i wklejenia rzeczywistego dowodu w `verification.md`.
- AI **nie zatwierdza** planów, PR ani wdrożeń — może działać do bramki produkcyjnej, ale jej nie przekracza.

---

## 5. Egzekucja reguł (hooki Claude Code i git)

Instrukcje w `CLAUDE.md` są doradcze. Reguły krytyczne są dodatkowo egzekwowane deterministycznie:

| Reguła | Mechanizm |
|---|---|
| Kod tylko według zatwierdzonego planu | Hook `PreToolUse` → `.claude/hooks/pre-edit-guard.ps1` (tylko plany `approved`; plan `done` niczego nie autoryzuje) |
| Bramki działają też w worktree | Ten sam hook: korzeń projektu = korzeń repozytorium git edytowanego pliku |
| Plan zatwierdza człowiek | Ten sam hook: blokada nadania `approved` (także powrotu z `done`), pytanie przy zmianie zatwierdzonego planu |
| Test-first przy naprawach (`Tryb: fix`) | Ten sam hook: blokada edycji testów zacommitowanych w `HEAD` |
| Brak sekretów w repozytorium | Ten sam hook: skan zapisywanej treści |
| Formatowanie | Hook `PostToolUse` → `.claude/hooks/post-edit-format.ps1` + `format.json` (komenda jako lista argumentów, bez powłoki) |
| Kod tylko według zatwierdzonego planu — co jest wolne | Bez planu: wyłącznie `docs/` i główne `CLAUDE.md`, `AGENTS.md`, `REVIEW.md`, `README.md` (ta sama lista w hooku, `New-PullRequest` i `Test-TopicReady` — funkcja `Test-FreePath`). Pozostałe pliki, także w `.claude/`, muszą być w planie |
| Agent nie zmienia własnych bramek | Hook + `permissions.deny` dla `.claude/settings*.json`, `.claude/hooks/`, `.claude/agents/`, `.claude/skills/`, `.claude/sdlc.json`, `.githooks/`, `tools/`, `.slopwatch/`, `dotnet-tools.json` (deny obejmuje też `sed`, `tee` i przekierowania w Bash) |
| PR akceptuje człowiek | Hook edycji: blokada nadania `Status: approved` w `pr.md`, pytanie przy zmianie zatwierdzonego PR |
| Rozdział ról autor / recenzent | Hook edycji: `review.md` zapisuje wyłącznie agent `sdlc-reviewer` (pole `agent_type`), a recenzent nie może zapisać nic innego; recenzent nie ma powłoki (diff dostaje z `.claude/sdlc-tmp/`) |
| Skille techniczne dostępne w projekcie | Pluginy stosu w `.claude/settings.json` (`enabledPlugins` + `extraKnownMarketplaces`, wstawia `Init-SDLC.ps1 -Stack`) — Claude Code proponuje instalację po zaufaniu folderowi; bez kopii skilli w repozytorium |
| Właściwe skille dla zmiany | Routing w `CLAUDE.md` (obszar → pliki → skille) + sekcja *Wymagane skille* w `plan.md`; recenzent ładuje je narzędziem Skill i wypisuje w `review.md` → *Zastosowane skille* |
| Reguły ze skilli, których agent nie może sam „zaakceptować” | `.slopwatch/` tylko dla człowieka (hook + deny, w tym `slopwatch --update-baseline`); zmiana wzorca snapshotu `*.verified.*` wymaga potwierdzenia człowieka (ask); bramka *Slopwatch* w `Test-TopicReady` (aktywna, gdy istnieje `.slopwatch/baseline.json`; narzędzie lokalne z manifestu `dotnet-tools.json` (w `.config/` lub w korzeniu), przywracane automatycznie przez `dotnet tool restore`; manifest tylko dla człowieka, agent ma zakaz `dotnet tool install/update/uninstall`, `slopwatch init` i `--create/--update-baseline`; tryb ścisły `--fail-on warning`; dodanie `[SlopwatchSuppress]` wymaga potwierdzenia człowieka). Ograniczenie narzędzia (0.4.2): atrybuty z pełną nazwą, np. `[Xunit.Fact(Skip = ...)]`, nie są wykrywane — wyłapuje je recenzent |
| Agent nie commituje na `main`/`master` | Hook `PreToolUse` Bash (`if: Bash(git *)`) → `.claude/hooks/pre-bash-guard.ps1`: blokada `commit/merge/cherry-pick/revert/am/rebase/pull` na `main`/`master` |
| Push tylko po GO | Hook git `.githooks/pre-push` (`core.hooksPath` ustawia Init) → `tools/Test-TopicReady.ps1`; agent ma zakaz `--no-verify` i zmiany `core.hooksPath` |
| Globalne `disableAllHooks: true` nie wyłącza bramek | `"disableAllHooks": false` w `.claude/settings.json` (projekt ma pierwszeństwo przed ustawieniami użytkownika; `settings.local.json` chroni hook) |
| Awaria hooka nie otwiera bramki | `"onFailure": "block"` + hook kończy każdy nieoczekiwany błąd blokadą (fail-closed); wymaga Claude Code ≥ 2.1.295 |
| Brak pushy do `main`, `--force`, `--no-verify`; każdy push wymaga potwierdzenia | `permissions.deny` / `ask` w `.claude/settings.json` |
| Review według `REVIEW.md` | Lokalnie: `/sdlc-review` / `tools/Invoke-Review.ps1` (doradczo) — źródłem prawdy dla bramek jest `review.md`. Opcjonalnie w CI: `.github/workflows/claude-review.yml` (`Init-SDLC.ps1 -IncludeCI`) — komentarze w PR na zdalnym repozytorium, dodatkowe wobec `review.md` (nie zna skilli z routingu i nie zastępuje bramek) |
| Merge tylko po akceptacji człowieka | Ręczny merge na zdalnym repozytorium; po skonfigurowaniu zdalnego repo warto włączyć tam branch protection na `main` |

**Wymagania**: Windows PowerShell (`powershell`) — na Linux/macOS potrzebny `pwsh` dostępny pod nazwą `powershell` (opcjonalny workflow `claude.yml` tworzy taki link na runnerze); hook `pre-push` wybiera `powershell` lub `pwsh` sam. Zmiany w `.claude/` wprowadza człowiek poza sesją agenta.

**Skille a proces**: skille są doradczą wiedzą (jak pisać kod), a nie mechanizmem egzekucji — reguła, której złamanie jest niedopuszczalne, musi mieć hook albo warunek w `Test-TopicReady`. Instrukcje skilli nie mają pierwszeństwa przed procesem: jeśli skill każe zmienić `.claude/`, `.githooks/`, `tools/` lub baseline narzędzia, agent zgłasza to człowiekowi. Nowy skill organizacyjny (np. polityka bezpieczeństwa) dodaje człowiek do `.claude/skills/` i do routingu w `CLAUDE.md`.

**Niewspierane równolegle**: plugin `devflow` (własne hooki PreToolUse/PostToolUse dublujące bramki SDLC, własny review z werdyktem APPROVE sprzeczny z zasadą „agent nie zatwierdza”, własna sekcja w `CLAUDE.md`). Nie włączaj go w projektach z tym procesem.

**Ograniczenia**: bramka planu kontroluje narzędzia edycji agenta, a nie dowolne polecenia powłoki (np. skrypt, który sam zapisuje pliki) — dlatego ostateczną bramką pozostaje decyzja człowieka w `pr.md` i ręczny merge. Człowiek może świadomie pominąć hook `pre-push` (`git push --no-verify`) — to celowe i widoczne w historii zdalnego repozytorium. W organizacji reguły nienegocjowalne warto przenieść do *managed settings* (centralnie zarządzanych, nienadpisywalnych przez projekt).
