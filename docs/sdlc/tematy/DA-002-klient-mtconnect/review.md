# Review: DA-002 — Klient MTConnect w workerze

Status: no-blocking-findings. Commit: d11c13f. Baza: 417e369. Recenzent: sdlc-reviewer. Data: 2026-11-04.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review DA-002` lub `tools/Invoke-Review.ps1 -Id DA-002`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|
| R-01 | P2 | `Worker/MTConnect/MTConnectSynchronizer.cs:84-90`, `:125-136` | (iteracja 1) Po `OUT_OF_RANGE` worker wznawiał od `nextSequence` z `/current`, pomijając dane wciąż obecne w buforze agenta, a `Warning` zaniżał wielkość luki. **Iteracja 2**: naprawione w `bf41614` i `6538cca`. Po resynchronizacji `/sample` startuje od `firstSequence` (przy pełnym buforze z zapasem `min(SampleCount, bufferSize/10)`, `MTConnectSynchronizer.cs:151-154`) i idzie od razu (`CycleAsync` zwraca `TimeSpan.Zero`, `:76`, `:89`). `Warning` podaje `[lostFrom, resumeSequence − 1]` (`:128-133`), czyli faktycznie utracony zakres. Pokrywają to testy `StepAsync_OutOfRange_ResumesFromBufferWithMarginAndWarnsAboutExactLostRange`, `…SmallFullBuffer…` i `…NotFullBuffer…` (`MTConnectSynchronizerTests.cs:174-231`) oraz przebiegi ręczne 5 i 6. Spec pkt 2.3–2.4 i 3.3 zaktualizowany, odstępstwa 6–7 w planie. | naprawiona |
| R-02 | P3 | `docs/sdlc/tematy/DA-002-klient-mtconnect/release.md:3-24` | (iteracja 1) `release.md` był pustym szablonem. **Iteracja 2**: wypełniony. Zawiera tabelę wszystkich kluczy `DataAcquisition:MTConnect:*` z odpowiadającymi im zmiennymi środowiskowymi, wartościami domyślnymi i zakresami, ostrzeżenie o `AgentUrl`, komendę `publish`, rollback i monitoring. | naprawiona |
| R-03 | P3 | `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs:185-186` | (iteracja 1) Słabe asercje `Assert.Contains("4", …)`. **Iteracja 2**: asercje sprawdzają wartości strukturalne logu `LostFrom`/`LostTo`/`InstanceId` przez `GetStructuredStateValue` (`:191-193`, `:213`, `:230`). | naprawiona |
| R-04 | P3 | `Worker/MTConnect/MTConnectSynchronizer.cs:84-90`, `:151-154` | Nowe po R-01: po resynchronizacji brak zabezpieczenia przed powtarzającym się `OUT_OF_RANGE`. Załóżmy, że między odpowiedzią `/current` a pierwszym `/sample` agent dopisze więcej obserwacji niż wynosi zapas `min(SampleCount, bufferSize/10)`. Przy domyślnych ustawieniach i ~100 obs./s symulatora zapas to ~10 s. Może się tak stać przy szybszym agencie albo w DA-003, gdy dostarczenie migawki `/current` do API (`DeliverAsync`, `:135`) potrwa dłużej. Wtedy `/sample?from=<resume>` znowu zwraca `OUT_OF_RANGE`, a cykl zamyka się w pętli: `OUT_OF_RANGE` → `/current` → dostarczenie migawki → `/sample` → `OUT_OF_RANGE`. Pętla nie ma opóźnienia, bo obie ścieżki zwracają `TimeSpan.Zero` i nie przechodzą przez backoff. Każdy obrót loguje `Warning` + `Information`, ponownie wysyła migawkę i nie dostarcza ani jednej obserwacji z `/sample`. Przed R-01 taka pętla nie była możliwa, bo wznowienie od `nextSequence` zawsze trafiało w bufor. W konfiguracji DA-002 (symulator, `LoggingObservationSink`) problem nie wystąpi, a przebieg 5 to potwierdza. Sugestia: jeśli `OUT_OF_RANGE` przyjdzie na pierwsze `/sample` po synchronizacji, wznowić od `nextSequence` z `/current` (zachowanie sprzed R-01) albo zastosować backoff. Dobrze byłoby też dodać test na dwa kolejne `OUT_OF_RANGE`. Można odłożyć do DA-003, gdzie odbiorca staje się wolny. | otwarta |

## Zgodność z planem

Wszystkie zmienione pliki spoza `docs/` są objęte planem (tabela w `pr.md`: brak pozycji „NIE”). Należą do nich `Worker/MTConnect/` (kroki 2 i 4), `Worker/Worker.cs`, `Worker/Program.cs`, `Worker/DataAcquisition.Worker.csproj` (krok 7) oraz `Worker.Tests/` z `Fixtures/` (kroki 1, 3, 8). Zmiany od iteracji 1 (`bf41614`, `6538cca`) dotyczą wyłącznie `Worker/MTConnect/` (synchronizator, model `StreamsHeader.BufferSize`/`IsBufferFull`, parser `bufferSize`), testów w `Worker.Tests/MTConnect/` i dokumentacji. API i `Contracts/` są bez zmian.

Odstępstwa 1–7 są opisane w `plan.md`. Nowe w tej iteracji:
- (6) wznowienie z bufora po R-01: decyzja człowieka. Zmieniono oczekiwania 3 testów, w tym oczekiwane `from` w teście regresji z `3cb00ba`. `Tryb: feature`, zmiana jawnie opisana i wynika ze zmiany zachowania, a nie z osłabienia testu: test nadal wymaga `current` → `probe`, logu restartu i braku `Warning`;
- (7) dwa błędy pierwszej poprawki R-01 wykryte w teście ręcznym, z czerwonym przebiegiem 51/54 przed `6538cca`.

`spec.md` (pkt 2.3, 2.4, 3.3) jest spójny z kodem.

## Pokrycie kryteriów akceptacji

| AC | Test / dowód w verification.md | Ocena recenzenta |
|---|---|---|
| AC-01 | Build/test/format z kodami wyjścia dla `6538cca` (Worker.Tests 54/54, Api.Tests 82/82); `dotnet publish` z `release.md` — kod 0. | pokryte |
| AC-02 | `MTConnectParserTests` (fixtures z symulatora i ręczne); nagłówek porównywany jako cały `StreamsHeader` łącznie z `BufferSize` (`MTConnectParserTests.cs:26`, `:41`, `:79`); brak atrybutu nagłówka → `MTConnectFormatException`. | pokryte |
| AC-03 | `StepAsync_FirstStep_…`, `StepAsync_ContinuousSampling_…` (ścisła lista URL-i, sekwencji i opóźnień, w tym `Zero` po synchronizacji), `StepAsync_AgentHasMoreData_…`; test ręczny (0 luk). | pokryte |
| AC-04 | `StepAsync_InstanceIdChanged_…` (wznowienie od `firstSequence` nowej instancji), `StepAsync_RestartedAgentAnswersOutOfRange_…`; przebieg 6 (log restartu, nowa instancja od 1, 0 luk). | pokryte |
| AC-05 | Trzy testy `OUT_OF_RANGE` (pełny bufor, mały pełny bufor, niepełny bufor) z asercjami na strukturalnych `LostFrom`/`LostTo`; przebieg ręczny 5 (`--buffer 1000`). | pokryte; ryzyko pętli przy niewystarczającym zapasie — R-04 (P3) |
| AC-06 | `StepAsync_TransientError_…` (5 błędów), `StepAsync_RepeatedErrors_…` (1→30 s, reset), `StepAsync_SinkFails_…`, `StepAsync_CurrentFailsAfterProbe_…`, testy klienta. | pokryte |
| AC-07 | `StartAsync_InvalidConfiguration_…` (7 przypadków), `Options_NoConfiguration_…`, `StopAsync_WhileRequestInProgress_…`, `RunAsync_Stopped…` (2). | pokryte |
| AC-08 | Przebieg 2 (`96142b5`) i przebieg 6 (`6538cca`) — rzeczywiste fragmenty logu, analiza ciągłości skryptem, 0 luk. | pokryte (dowód ręczny) |

## Dowody weryfikacji

`verification.md` ma status `verified` dla commita `6538cca`. Przejrzany HEAD `d11c13f` różni się od niego tylko commitami `10049e8` i `d11c13f`. Według opisów commitów i `pr.md` dotyczą one wyłącznie dokumentacji (`verification.md`, `pr.md`), więc dowody obejmują przeglądany kod. Recenzent nie ma powłoki i nie zweryfikował tego niezależnie.

Dowody są rzeczywiste:
- kody wyjścia mierzone bez potoku;
- czerwone przebiegi przed zmianami po review (49/53, 51/54);
- przebieg 4, który wykazał błędy pierwszej poprawki;
- przebiegi 5 i 6 z fragmentami logu.

Lista testów w sekcji *Lista testów workera* dotyczy `96142b5` (52 testy). Różnica do 54 testów jest jawnie opisana (1 test zastąpiony, 2 nowe).

Pominięcia są jawne i zasadne:
- `OUT_OF_RANGE` na pełnym buforze 131072 na żywo;
- timeout/5xx na żywo;
- realny cppagent;
- zatrzymanie przez `timeout` zamiast Ctrl+C.

Logi nie zawierają wartości obserwacji. W diffie nie ma sekretów.

## Historia iteracji

| Iteracja | Commit | Data | Wynik |
|---|---|---|---|
| 1 | 68c98af | 2026-11-04 | changes-requested: P1 0, P2 1, P3 2 |
| 2 | d11c13f | 2026-11-04 | no-blocking-findings: R-01, R-02, R-03 naprawione; nowe R-04 (P3); otwarte P1 0, P2 0, P3 1 |
