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
- `.claude/` (settings, hooks, agents, skills, sdlc.json), `.githooks/`, `tools/`, `.slopwatch/` i manifest `dotnet-tools.json` zmienia tylko człowiek; zakaz `dotnet tool install/update/uninstall`, `slopwatch init` i zmian baseline;
- w `Tryb: fix` zacommitowane testy są zablokowane;
- dodanie `[SlopwatchSuppress]` i zmiana wzorców snapshotów `*.verified.*` wymagają potwierdzenia człowieka;
- skan sekretów w każdej zapisywanej treści;
- na `main`/`master` blokada `commit`, `merge`, `rebase`, `pull`, `cherry-pick`, `revert` i `am`; blokada `git push` na `main`/`master`, `--force` i `--no-verify`.

Jeśli hook zablokuje operację — nie obchodź go (np. przez `Bash`/`sed`). Zgłoś blokadę człowiekowi.

---

## 🧭 Skille

- **Skille procesu**: `/sdlc-pr <ID>` (lokalny PR w `pr.md`) i `/sdlc-review <ID>` (niezależne review w `review.md`).
- **Skille techniczne** pochodzą z pluginów zadeklarowanych w `.claude/settings.json` (Claude Code zaproponuje ich instalację po zaufaniu folderowi). Przed zmianą pliku z danego obszaru załaduj skille z tabeli poniżej i wpisz je w `plan.md` → *Wymagane skille*. Recenzent sprawdza zgodność z tymi samymi skillami.
- Skille są doradcze — reguły krytyczne egzekwują hooki i bramki `tools/Test-TopicReady.ps1`.
- Jeśli skill każe zmienić `.claude/` (settings, hooks, agents, skills), `.githooks/`, `tools/` albo baseline narzędzia (np. `.slopwatch/`) — nie rób tego; zgłoś propozycję człowiekowi.

### Routing skilli — stos .NET

Skille pochodzą z pluginów zadeklarowanych w `.claude/settings.json` (`dotnet-skills`, `microsoft-docs`). Przed zmianą pliku z danego obszaru załaduj wskazane skille (narzędzie Skill); w `plan.md` wpisz je w sekcji *Wymagane skille*.

| Obszar | Pliki (przykłady) | Skille |
|---|---|---|
| Kod C# — zawsze | `**/*.cs` | `dotnet-skills:csharp-coding-standards`, `dotnet-skills:csharp-nullable-reference-types` |
| Ścieżki krytyczne wydajnościowo, modele domeny | typy domenowe, hot paths | `dotnet-skills:csharp-type-design-performance` |
| Publiczne API, kontrakty, NuGet | `**/Contracts/**`, publiczne typy bibliotek | `dotnet-skills:csharp-api-design` |
| Współbieżność, async, kolejki | `Task`, `Channel`, background services | `dotnet-skills:csharp-concurrency-patterns` |
| EF Core, zapytania, migracje | `**/*DbContext*.cs`, `**/Migrations/**`, repozytoria | `dotnet-skills:efcore-patterns`, `dotnet-skills:database-performance` |
| Serializacja, formaty wymiany | DTO, JSON, Protobuf | `dotnet-skills:serialization` |
| DI i konfiguracja | `Program.cs`, `**/ServiceCollectionExtensions*.cs`, `appsettings*.json` | `dotnet-skills:microsoft-extensions-dependency-injection`, `dotnet-skills:microsoft-extensions-configuration` |
| Testy integracyjne z bazą / infrastrukturą | `tests/**/*Integration*` | `dotnet-skills:testcontainers` |
| Testy snapshotów | `**/*.verified.*` | `dotnet-skills:snapshot-testing` |
| Testy UI Blazor | `tests/**/*Playwright*`, `*.razor` | `dotnet-skills:playwright-blazor` |
| Testy property-based / modelowe | generatory, specyfikacje | `dotnet-skills:cscheck` |
| Solucja, projekty, pakiety | `*.sln(x)`, `*.csproj`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` | `dotnet-skills:project-structure`, `dotnet-skills:package-management` |
| Jakość zmian (po każdej zmianie kodu) | cała zmiana | `dotnet-skills:slopwatch`; przy ocenie ryzyka: `dotnet-skills:crap-analysis` |
| API Microsoft / Azure — sprawdzenie sygnatur | użycie SDK Microsoft | `microsoft-docs:microsoft-code-reference` |

Uwagi dla stosu .NET:
- Zmiany struktury solucji (`Directory.Build.props`, `global.json`, `.slnx`) wymagają osobnego kroku w planie, a przy większej zmianie — ADR.
- Slopwatch jest narzędziem lokalnym projektu (manifest `dotnet-tools.json`, wersja przypięta) — uruchamiaj go przez `dotnet tool run slopwatch analyze --fail-on warning`, nie instaluj globalnie. Wykrycia naprawiaj w kodzie — nie dodawaj `[SlopwatchSuppress]` (wyjątek akceptuje człowiek; hook pyta o zgodę). Skill `slopwatch` opisuje też dodanie hooka do `.claude/settings.json` i aktualizację baseline — obie rzeczy wykonuje wyłącznie człowiek (bramka slopwatch jest już w `tools/Test-TopicReady.ps1`).
- Wzorce snapshotów `*.verified.*` akceptuje człowiek (hook pyta o zgodę przy każdej zmianie).

---

## 🛠️ Komendy deweloperskie

Jedna komenda na czynność — agent musi móc sam zweryfikować swoją pracę. To jedyne miejsce z komendami (`docs/sdlc/03-build/PRACA-Z-KODEM.md` odsyła tutaj). Komenda testów musi być **tą samą komendą** co `testCommand` w `.claude/sdlc.json`, którą uruchamia bramka przed pushem. Uruchamiaj z katalogu głównego repozytorium (.NET SDK 10).

```bash
# Budowanie projektu
dotnet build DataAcquisition.slnx

# Uruchamianie testów (= testCommand w .claude/sdlc.json; xUnit v3)
dotnet test DataAcquisition.slnx --nologo

# Lint / formatowanie (weryfikacja, kod 0 = brak zmian do wprowadzenia)
dotnet format DataAcquisition.slnx --verify-no-changes

# Jakość zmian (slopwatch, narzędzie lokalne z .config/dotnet-tools.json)
# Wymaga .slopwatch/baseline.json — tworzy go człowiek (`slopwatch init`); bez baseline kod wyjścia 2.
dotnet tool run slopwatch analyze --fail-on warning

# Symulator CNC / agent MTConnect (Python 3.9+, http://localhost:5000)
py resources/cnc_mtconnect_sim.py --seed 42

# Uruchomienie API (klucz: User Secrets DataAcquisition:Api:ApiKey) i workera
dotnet run --project Api
dotnet run --project Worker/DataAcquisition.Worker.csproj
```

---

## ⚠️ Powtarzające się błędy

- Opis w `pr.md` nieaktualny po poprawkach z review (liczba testów, commit dowodów, lista odstępstw) → po każdej rundzie poprawek odśwież sekcje opisowe `pr.md`, nie tylko dane automatyczne.
- Blok kodu z potrójnymi backtickami w `plan.md` psuje rozpoznawanie ścieżek przez `Test-PlanCovers` (ścieżki po bloku są „spoza planu”) → w `plan.md` komendy zapisuj jako listę z kodem inline.
- Kod wyjścia mierzony za potokiem (`cmd | tail; echo $?`) pokazuje kod `tail`, nie komendy → do dowodów w `verification.md` przekierowuj output do pliku i bierz `$?` bezpośrednio.

Przed oddaniem zmian do review (`/sdlc-pr`) upewnij się, że kod spełnia kryteria opisane w [REVIEW.md](REVIEW.md).
