# Review: DA-002 — Klient MTConnect w workerze

Status: changes-requested. Commit: 68c98af. Baza: 417e369. Recenzent: sdlc-reviewer. Data: 2026-11-04.

> Plik zapisuje wyłącznie recenzent (`/sdlc-review DA-002` lub `tools/Invoke-Review.ps1 -Id DA-002`) — autor zmian ma blokadę zapisu. Człowiek może ręcznie zmienić status uwagi na `odrzucona` z uzasadnieniem.
> Statusy: not-run → changes-requested / no-blocking-findings. `Commit` = przejrzany commit; po każdej zmianie kodu review trzeba powtórzyć.
> Uwagi są doradcze: recenzent nie zatwierdza zmian — decyzję podejmuje człowiek w `pr.md`.

## Uwagi

| ID | Priorytet | Plik:linia | Scenariusz i uzasadnienie | Status |
|---|---|---|---|---|
| R-01 | P2 | `Worker/MTConnect/MTConnectSynchronizer.cs:84-90`, `:125-136` | Po `OUT_OF_RANGE` worker robi `/current` i ustawia `_nextSequence = current.Header.NextSequence`, a `Warning` podaje jako utracone tylko `[lostFrom, FirstSequence − 1]`. Obserwacje `[FirstSequence, NextSequence − 1]`, które **nadal są w buforze agenta**, też nie zostaną dostarczone — z tego zakresu trafia do odbiorcy tylko migawka (po jednej ostatniej wartości na dataItem). Scenariusz realny już w DA-003: API niedostępne > ~22 min → odbiorca rzuca wyjątek, worker ponawia ten sam `from` z backoffem do 30 s → bufor się przesuwa → `OUT_OF_RANGE`. Przy `nextSequence=1000`, `firstSequence=20000`, `nextSequence` agenta `151072` log mówi o utracie 1000–19999, a faktycznie pominiętych jest jeszcze ~131 000 obserwacji, dostępnych do pobrania. Test `StepAsync_OutOfRange_…` (`MTConnectSynchronizerTests.cs:171-187`) utrwala to zachowanie: z zakresu 100–200 dostarczona jest tylko 150, a ostrzeżenie dotyczy 4–99. Kłóci się to z celem z `intent.md` („bez luk”) i z AC-05 („log Warning z wielkością utraconego zakresu”): podana wielkość jest zaniżona, więc operator nie ma wiarygodnej informacji o skali luki. Zachowanie jest zgodne z `spec.md` 3.3, więc decyzja należy do człowieka. Do wyboru: (a) po `OUT_OF_RANGE` wznowić `/sample` od `firstSequence` z `/current` (z marginesem albo ponowieniem, gdy bufor przesunie się między żądaniami), co ograniczy stratę do danych naprawdę usuniętych z bufora; albo (b) co najmniej logować faktycznie pominięty zakres `[lostFrom, current.NextSequence − 1]` i zaktualizować spec i ryzyka w `pr.md`. | otwarta |
| R-02 | P3 | `docs/sdlc/tematy/DA-002-klient-mtconnect/release.md:3-24` | `release.md` jest nadal pustym szablonem (`Commit: 417e369`, brak treści), a zmiana wprowadza nową konfigurację `DataAcquisition:MTConnect:*`. Domyślny `AgentUrl` to `http://localhost:5000`, więc wdrożenie na serwer bez `DataAcquisition__MTConnect__AgentUrl` skończy się tym, że worker nie zbiera danych i tylko loguje `Warning` co 30 s. Instrukcja jest w `plan.md` (*Wydanie*) i `pr.md`, ale REVIEW.md (filar 5) oczekuje jej w `release.md`. Wystarczy przenieść tam listę kluczy z domyślnymi wartościami przed wydaniem. | otwarta |
| R-03 | P3 | `Worker.Tests/MTConnect/MTConnectSynchronizerTests.cs:185-186` | `Assert.Contains("4", …)` i `Assert.Contains("99", …)` to słabe asercje: „4” pasuje do dowolnej liczby zawierającej tę cyfrę (np. identyfikatora instancji w treści komunikatu). Gdyby regresja zmieniła granicę zakresu na np. 14–99 albo 4–199, test nadal by przechodził. Lepiej sprawdzić dokładny fragment (`"4-99"`) albo parametry strukturalne logu (`LostFrom`/`LostTo` z `FakeLogRecord`). | otwarta |

## Zgodność z planem

Wszystkie zmienione pliki spoza `docs/` są objęte planem: `Worker/MTConnect/` (kroki 2 i 4), `Worker/Worker.cs`, `Worker/Program.cs`, `Worker/DataAcquisition.Worker.csproj` (pakiet `Microsoft.Extensions.Http` 10.0.12 i `InternalsVisibleTo` — zgodnie z krokiem 7), `Worker.Tests/` z `Fixtures/`. Tabela w `pr.md` nie ma pozycji „NIE”. API i `Contracts/` nie są zmienione.

Odstępstwa 1–5 są opisane w `plan.md` z uzasadnieniem:
- (1) named client zamiast typed: uzasadnienie (rotacja handlerów w singletonie) jest poprawne, a `BaseAddress` z końcowym `/` ma test;
- (2) ręczny `sample-empty.xml`;
- (3) test-first tylko dla parsera i synchronizatora;
- (4) tempo symulatora;
- (5) wykrywanie restartu w `SynchronizeAsync`: zmiana zgodna z intencją AC-04, z testem regresji `3cb00ba`.

`Tryb: feature`. Dla błędu znalezionego w teście ręcznym zachowano kolejność: najpierw czerwony test (`3cb00ba`), potem poprawka (`96142b5`). Dowód czerwonego przebiegu jest w `verification.md`. Recenzent nie ma powłoki, więc niezmienności testu między `3cb00ba` a `96142b5` nie zweryfikował niezależnie. Bieżąca treść testu (`MTConnectSynchronizerTests.cs:149-167`) sprawdza jednak dokładnie opisany scenariusz: `current` → `probe`, log restartu, brak `Warning`.

## Pokrycie kryteriów akceptacji

| AC | Test / dowód w verification.md | Ocena recenzenta |
|---|---|---|
| AC-01 | Output `dotnet build`/`test`/`format` z kodami wyjścia dla `96142b5`. Zauważony i opisany problem z `tail` w potoku. | pokryte |
| AC-02 | `MTConnectParserTests` (15 przypadków) na fixtures z symulatora i ręcznych: CONDITION `Fault`/`Normal`, `UNAVAILABLE`, pusty `/sample`, obcy namespace, zły XML, brak atrybutu nagłówka. `type`/`subType` z `/probe` w `StepAsync_KnownDataItem_…`. | pokryte |
| AC-03 | `StepAsync_ContinuousSampling_…` (ścisła lista URL-i i sekwencji), `StepAsync_AgentHasMoreData_…`, `StepAsync_FirstStep_…`, testy URL klienta oraz analiza ciągłości w teście ręcznym (0 luk). | pokryte |
| AC-04 | `StepAsync_InstanceIdChanged_…`, `StepAsync_RestartedAgentAnswersOutOfRange_…` i test ręczny z restartem symulatora (log restartu, `/probe` nowej instancji). | pokryte |
| AC-05 | `StepAsync_OutOfRange_ResynchronizesWithoutProbeAndWarnsAboutLostRange`. Na symulatorze tylko test jednostkowy (opisane w pominięciach). | pokryte testem; wielkość luki w logu zaniżona (R-01), asercja słaba (R-03) |
| AC-06 | `StepAsync_TransientError_…` (5 rodzajów błędów), `StepAsync_RepeatedErrors_…` (1→30 s, reset), `StepAsync_SinkFails_…`, `StepAsync_CurrentFailsAfterProbe_…`, testy klienta (timeout, 500, 404 bez `MTConnectError`). Ręcznie sprawdzona tylko odmowa połączenia. | pokryte |
| AC-07 | `StartAsync_InvalidConfiguration_…` (7 przypadków z pełnym kluczem), `StopAsync_WhileRequestInProgress_…`, `RunAsync_StoppedWhileWaiting_…`, `RunAsync_StoppedDuringRequest_…`. | pokryte |
| AC-08 | Przebieg 2 dla `96142b5`: 129 paczek, 2 instancje, 0 luk, fragment logu z restartem. Przebieg 1 udokumentował znaleziony błąd. | pokryte (dowód ręczny, rzeczywisty fragment logu) |

## Dowody weryfikacji

`verification.md` ma status `verified` dla commita `96142b5`. Przejrzany HEAD `68c98af` różni się od niego wyłącznie commitami dokumentacji (`62a493c`, `107bc20`, `68c98af` — zgodnie z opisami commitów i listą plików w `pr.md`), więc dowody dotyczą przeglądanego kodu.

Dowody są rzeczywiste:
- kody wyjścia mierzone bez potoku;
- pełna lista 52 testów workera i 82 testów API;
- czerwone przebiegi test-first;
- log z testu ręcznego z analizą ciągłości.

Pominięcia są jawne i zasadne: `OUT_OF_RANGE` na żywo, timeout/5xx na żywo, realny cppagent, zatrzymanie przez `timeout` zamiast Ctrl+C (pokryte testami AC-07). Logi nie zawierają wartości obserwacji ani treści odpowiedzi. W diffie nie ma sekretów.

## Historia iteracji

| Iteracja | Commit | Data | Wynik |
|---|---|---|---|
| 1 | 68c98af | 2026-11-04 | changes-requested: P1 0, P2 1, P3 2 |
