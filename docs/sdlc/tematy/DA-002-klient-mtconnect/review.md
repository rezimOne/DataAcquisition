# Review: DA-002 — Klient MTConnect w workerze

Status: no-blocking-findings. Commit: f6855f7. Baza: 417e369. Recenzent: sdlc-reviewer. Data: 2026-11-04.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review DA-002` lub `tools/Invoke-Review.ps1 -Id DA-002`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|
| R-01 | P2 | `Worker/MTConnect/MTConnectSynchronizer.cs:84-90`, `:125-136` | (iteracja 1) Po `OUT_OF_RANGE` worker wznawiał od `nextSequence` z `/current`, pomijając dane wciąż obecne w buforze agenta, a `Warning` zaniżał wielkość luki. **Iteracja 2**: naprawione w `bf41614` i `6538cca`. Po resynchronizacji `/sample` startuje od `firstSequence` (przy pełnym buforze z zapasem `min(SampleCount, bufferSize/10)`) i idzie od razu (`CycleAsync` zwraca `TimeSpan.Zero`). `Warning` podaje `[lostFrom, resumeSequence − 1]`, czyli faktycznie utracony zakres. Pokrywają to testy `StepAsync_OutOfRange_ResumesFromBufferWithMarginAndWarnsAboutExactLostRange`, `…SmallFullBuffer…` i `…NotFullBuffer…` oraz przebiegi ręczne 5 i 6. **Iteracja 3**: bez regresji. Po poprawce R-04 numery linii to `MTConnectSynchronizer.cs:132-141` (wybór `resumeSequence` i `Warning`) oraz `:161-164` (`ResumeSequence`). | naprawiona |
| R-02 | P3 | `docs/sdlc/tematy/DA-002-klient-mtconnect/release.md:3-24` | (iteracja 1) `release.md` był pustym szablonem. **Iteracja 2**: wypełniony. Zawiera tabelę kluczy `DataAcquisition:MTConnect:*` ze zmiennymi środowiskowymi, wartościami domyślnymi i zakresami, ostrzeżenie o `AgentUrl`, komendę `publish`, rollback i monitoring. | naprawiona |
| R-03 | P3 | `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs:185-186` | (iteracja 1) Słabe asercje `Assert.Contains("4", …)`. **Iteracja 2**: asercje sprawdzają wartości strukturalne logu `LostFrom`/`LostTo`/`InstanceId` przez `GetStructuredStateValue`. Nowy test R-04 też korzysta z tego wzorca (`:221-223`). | naprawiona |
| R-04 | P3 | `Worker/MTConnect/MTConnectSynchronizer.cs:84-90`, `:151-154` | (iteracja 2) Po resynchronizacji nic nie chroniło przed powtarzającym się `OUT_OF_RANGE`. Jeśli agent dopisał więcej obserwacji niż wynosi zapas, cykl `OUT_OF_RANGE` → `/current` → `/sample` zapętlał się bez opóźnienia. **Iteracja 3**: naprawione w `d83768a`. `CycleAsync` ustawia `_skipBufferOnResync = _resumedFromBuffer` przy `OUT_OF_RANGE` (`MTConnectSynchronizer.cs:88-93`). Wtedy `SynchronizeAsync` wznawia od `nextSequence` (`:132-134`). `_resumedFromBuffer` jest zerowane dopiero po udanym dostarczeniu paczki `/sample` (`:107`). Flaga przeżywa nieudane `/current`, bo jest czyszczona dopiero na końcu udanej synchronizacji (`:146`), więc błąd przejściowy w trakcie resync nie przywraca pętli. Na tym samym końcu synchronizacji `_resumedFromBuffer` dostaje wartość `resumeSequence < nextSequence` (`:147`). Dzięki temu po skoku do „teraz” następne `OUT_OF_RANGE` znów wznawia z bufora. Test `StepAsync_OutOfRangeRightAfterResumeFromBuffer_FallsBackToNextSequenceInsteadOfLooping` (`MTConnectSynchronizerTests.cs:196-224`) sprawdza pełną sekwencję URL-i: wznowienie z bufora → `OUT_OF_RANGE` → skok do `nextSequence` → udany `/sample` → kolejne `OUT_OF_RANGE` → znowu wznowienie z bufora z zapasem. Sprawdza też dokładne zakresy trzech `Warning`. Czerwony przebieg (`Expected 232572`, `Actual 102500`) jest w `verification.md`. Spec pkt 2.3 i odstępstwo 8 w planie zaktualizowane. | naprawiona |

## Zgodność z planem

Wszystkie zmienione pliki spoza `docs/` są objęte planem. W tabeli w `pr.md` nie ma pozycji „NIE”. Zmiany od iteracji 2 (`20da805`, `d83768a`) dotyczą wyłącznie `Worker/MTConnect/MTConnectSynchronizer.cs` (krok 4) i `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs` (krok 8), a pozostałe commity dokumentacji. API i `Contracts/` są bez zmian.

Odstępstwa 1–8 są opisane w `plan.md`. Nowe w tej iteracji jest odstępstwo (8): poprawka R-04 na decyzję człowieka. W czerwonym teście z `20da805` po commicie zmieniono tylko liczbę kroków (6 → 5), a oczekiwania zostały bez zmian. Autor deklaruje, że poprawiona wersja była czerwona na starym kodzie, co potwierdza przebieg w `verification.md`. Plan ma `Tryb: feature`, więc reguła niezmienności testu z trybu fix formalnie nie obowiązuje, a zmiana jest jawnie opisana. Liczba kroków jest spójna z kolejką fałszywego agenta: 4 odpowiedzi `/sample` i 3 `/current` dają 5 wywołań `StepAsync`.

`spec.md` pkt 2.3 (wyjątek R-04) jest spójny z kodem.

## Pokrycie kryteriów akceptacji

| AC | Test / dowód w verification.md | Ocena recenzenta |
|---|---|---|
| AC-01 | Build/test/format z kodami wyjścia dla `d83768a`: Worker.Tests 55/55, Api.Tests 82/82, `FORMAT_EXIT=0`. `dotnet publish` z `release.md` daje kod 0 (na `bf41614`). | pokryte |
| AC-02 | `MTConnectParserTests` na fixtures z symulatora i ręcznych. Nagłówek jest porównywany jako cały `StreamsHeader` łącznie z `BufferSize`. Brak atrybutu nagłówka daje `MTConnectFormatException`. | pokryte |
| AC-03 | `StepAsync_FirstStep_…`, `StepAsync_ContinuousSampling_…` (ścisła lista URL-i, sekwencji i opóźnień), `StepAsync_AgentHasMoreData_…`; test ręczny (0 luk). | pokryte |
| AC-04 | `StepAsync_InstanceIdChanged_…`, `StepAsync_RestartedAgentAnswersOutOfRange_…`; przebieg ręczny 6 (log restartu, nowa instancja od 1, 0 luk). | pokryte |
| AC-05 | Cztery testy `OUT_OF_RANGE` (pełny bufor, mały pełny bufor, niepełny bufor i nowy test braku pętli po wznowieniu z bufora) z asercjami na strukturalnych `LostFrom`/`LostTo`; przebieg ręczny 5 (`--buffer 1000`). | pokryte |
| AC-06 | `StepAsync_TransientError_…` (5 błędów), `StepAsync_RepeatedErrors_…` (1→30 s, reset), `StepAsync_SinkFails_…`, `StepAsync_CurrentFailsAfterProbe_…`, testy klienta. | pokryte |
| AC-07 | `StartAsync_InvalidConfiguration_…` (7 przypadków), `Options_NoConfiguration_…`, `StopAsync_WhileRequestInProgress_…`, `RunAsync_Stopped…` (2). | pokryte |
| AC-08 | Przebiegi 2 (`96142b5`) i 6 (`6538cca`): rzeczywiste fragmenty logu, analiza ciągłości skryptem, 0 luk. Poprawka R-04 zmienia tylko ścieżkę drugiego z rzędu `OUT_OF_RANGE`, której w tych przebiegach nie było, więc nie unieważnia dowodu. | pokryte (dowód ręczny) |

## Dowody weryfikacji

`verification.md` ma status `verified` dla commita `d83768a`. Przejrzany HEAD `f6855f7` różni się od niego tylko commitami `ec8083f` i `f6855f7`. Według opisów commitów i `pr.md` obejmują one wyłącznie dokumentację (`verification.md`, `pr.md`), więc dowody dotyczą przeglądanego kodu. Recenzent nie ma powłoki i nie zweryfikował tego niezależnie. Diff w `.claude/sdlc-tmp/DA-002-klient-mtconnect.diff` zawiera kod poprawki R-04 (`_skipBufferOnResync`, `_resumedFromBuffer`).

Dowody są rzeczywiste:
- kody wyjścia mierzone bez potoku;
- czerwony przebieg testu R-04 (54/55) przed poprawką, z konkretnym `Expected`/`Actual`;
- wcześniejsze czerwone przebiegi (49/53, 51/54) i przebiegi ręczne 4–6.

Lista testów w sekcji *Lista testów workera* dotyczy `96142b5` (52 testy). Różnica do 55 jest jawnie opisana: 1 test zastąpiony, 3 nowe w iteracjach po review.

Pominięcia są jawne i zasadne. Scenariusz R-04 nie był odtworzony na żywo, tylko testem jednostkowym; zostało to wpisane do `verification.md`. Pozostałe pominięcia:
- `OUT_OF_RANGE` na pełnym buforze 131072;
- timeout i 5xx na żywo;
- realny cppagent;
- zatrzymanie przez `timeout` zamiast Ctrl+C.

Logi nie zawierają wartości obserwacji. W diffie nie ma sekretów.

## Historia iteracji

| Iteracja | Commit | Data | Wynik |
|---|---|---|---|
| 1 | 68c98af | 2026-11-04 | changes-requested: P1 0, P2 1, P3 2 |
| 2 | d11c13f | 2026-11-04 | no-blocking-findings: R-01, R-02, R-03 naprawione; nowe R-04 (P3); otwarte P1 0, P2 0, P3 1 |
| 3 | f6855f7 | 2026-11-04 | no-blocking-findings: R-04 naprawiona (`d83768a`); brak nowych uwag; otwarte P1 0, P2 0, P3 0 |
