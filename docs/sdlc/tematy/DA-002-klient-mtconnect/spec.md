# Spec: DA-002 — Klient MTConnect w workerze

Status: approved. Odpowiedzialny: nog_programista2. Data: 2026-11-04. Źródło: [intent](intent.md).

## Zachowanie

### Stany i cykl synchronizacji

Worker ma dwa stany: **Niezsynchronizowany** i **Strumień** (`instanceId`, `nextSequence`).

1. **Start → Niezsynchronizowany.**
2. **Synchronizacja**:
   1. `GET /probe` (przy starcie i po zmianie `instanceId`) → mapa `dataItemId` → (`category`, `type`, `subType`, `units`) i lista urządzeń; log `Information` (liczba urządzeń i dataItemów).
   2. `GET /current` → migawka ostatnich wartości. Obserwacje migawki są **przekazywane do odbiorcy** (decyzja PO), posortowane po `sequence`.
   3. Zapamiętaj `Header/@instanceId` i pozycję wznowienia → stan **Strumień**. Pozycja wznowienia (zmiana po review R-01, decyzja człowieka 2026-11-04):
      - **pierwszy start workera**: `Header/@nextSequence` (start od „teraz”);
      - **resynchronizacja** (po `OUT_OF_RANGE` lub restarcie agenta): to, co agent ma jeszcze w buforze — `Header/@firstSequence`; gdy bufor jest pełny (`lastSequence − firstSequence + 1 ≥ bufferSize`), `min(firstSequence + SampleCount, nextSequence)`, bo pełny bufor kołowy usuwa najstarszą obserwację przy każdej nowej i start dokładnie od `firstSequence` dałby kolejne `OUT_OF_RANGE`.
      Migawka `/current` jest przekazywana zawsze; jej obserwacje mogą się powtórzyć w `/sample` z bufora (at-least-once).
3. **Strumień** — w pętli:
   1. `GET /sample?from=<nextSequence>&count=<SampleCount>`.
   2. `Header/@instanceId` ≠ zapamiętany → log `Information` („agent restarted”), przejście do synchronizacji **z** `/probe` (model urządzenia mógł się zmienić). Obserwacje z tej odpowiedzi nie są przekazywane (pobierze je `/current`/`/sample` nowej instancji).
   3. Odpowiedź `MTConnectError` z `errorCode="OUT_OF_RANGE"` (HTTP 404) → synchronizacja; `/probe` tylko, gdy `/current` pokaże inny `instanceId` (restart agenta — zwykle objawia się właśnie jako `OUT_OF_RANGE`, bo sekwencje nowej instancji są niższe). Przy tym samym `instanceId` log `Warning` z faktycznie utraconym zakresem `[nextSequence_stare, pozycja_wznowienia − 1]` (jeśli niepusty).
   4. W przeciwnym razie: obserwacje sortowane po `sequence` → przekazane do odbiorcy → `nextSequence = Header/@nextSequence`.
   5. **Doganianie**: jeśli `Header/@nextSequence ≤ Header/@lastSequence` (agent ma więcej danych niż zwrócił), kolejne żądanie idzie natychmiast; w przeciwnym razie czekamy `PollInterval`.
   6. Pusty `/sample` (brak `<Streams>` z obserwacjami) to nie błąd: odbiorca nie jest wołany, `nextSequence` bez zmian.

### Błędy i ponowienia

- **Błędy przejściowe**: brak połączenia (`HttpRequestException`), przekroczenie `RequestTimeout`, HTTP 5xx, HTTP 4xx inne niż `OUT_OF_RANGE`, niepoprawny XML, nieoczekiwany element główny, brak wymaganych atrybutów nagłówka → log `Warning` (typ błędu, adres, bez treści odpowiedzi), opóźnienie `RetryInitialDelay` podwajane do `RetryMaxDelay`; po pierwszym udanym cyklu opóźnienie wraca do początkowego. Stan się nie zmienia (ponawiamy ten sam krok z tym samym `from`).
- **Błąd odbiorcy** (wyjątek z `IObservationSink`): traktowany jak błąd przejściowy — `nextSequence` **nie** jest przesuwany, więc paczka zostanie pobrana i przekazana ponownie (dostarczenie *at-least-once*; duplikaty po stronie API są idempotentne — DA-001 AC-05).
- **Zatrzymanie hosta** (`CancellationToken`): przerwanie oczekiwania i żądania HTTP; `ExecuteAsync` kończy się bez wyjątku i bez logu błędu.
- Żaden błąd nie kończy hosta (pętla łapie wyjątki poza anulowaniem przy zatrzymaniu).

### Odbiorca w DA-002

`LoggingObservationSink`: dla każdej paczki log `Information`: urządzenie, `instanceId`, liczba obserwacji, zakres `sequence` (min–max), źródło (`current` / `sample`). Nie loguje wartości obserwacji.

## Dane i kontrakty

### Wejście — format agenta (MTConnect 1.3, jak w `resources/cnc_mtconnect_sim.py`)

Nagłówek `MTConnectStreams`:

```xml
<Header creationTime="2026-11-04T11:10:16.462Z" sender="cnc-sim-agent" instanceId="1793790556"
        version="1.3.0.0" bufferSize="131072" nextSequence="5873" firstSequence="1" lastSequence="5872"/>
```

Obserwacje (grupowane po komponentach — kolejność w XML ≠ `sequence`):

```xml
<DeviceStream name="VMC5AX" uuid="MAZAK-SIM-001">
  <ComponentStream component="Linear" name="X" componentId="x">
    <Samples>
      <Position dataItemId="xpos" timestamp="2026-11-04T11:10:16.400Z" sequence="5850" subType="ACTUAL">112.457</Position>
    </Samples>
  </ComponentStream>
  <ComponentStream component="Controller" name="controller" componentId="ctrl">
    <Condition>
      <Fault dataItemId="system" timestamp="…" sequence="5851" type="SYSTEM" nativeCode="AL-0123" qualifier="" severity="">SPINDLE OVERLOAD</Fault>
    </Condition>
  </ComponentStream>
</DeviceStream>
```

Błąd: `<MTConnectError><Header …/><Errors><Error errorCode="OUT_OF_RANGE">…</Error></Errors></MTConnectError>` (HTTP 404). Parser rozpoznaje dokument po elemencie głównym i namespace `urn:mtconnect.org:MTConnect{Devices|Streams|Error}:1.3`; inny namespace/wersja → błąd przejściowy z czytelnym komunikatem.

### Model w workerze (`Worker/MTConnect/`, niezależny od `Contracts/`)

| Pole | Typ | Źródło |
|---|---|---|
| `DeviceName` | string | `DeviceStream/@name` (np. `VMC5AX` — ta sama wartość posłuży jako `deviceId` w API) |
| `InstanceId` | long | `Header/@instanceId` |
| `DataItemId` | string | `@dataItemId` |
| `Sequence` | long | `@sequence` |
| `Timestamp` | DateTimeOffset (UTC) | `@timestamp` (ISO 8601 z `Z`; parsowanie niezależne od kultury) |
| `Category` | enum `Sample` / `Event` / `Condition` | element nadrzędny `Samples` / `Events` / `Condition` |
| `Type`, `SubType`, `Units` | string? | z `/probe` po `dataItemId`; dla CONDITION `type` z atrybutu elementu; brak w `/probe` → `Type` z nazwy elementu (`RotaryVelocity` → `ROTARY_VELOCITY`), log `Warning` raz na `dataItemId` |
| `Value` | string | tekst elementu (przycięty); `UNAVAILABLE` bez zmian; dla CONDITION `Normal` pusty tekst → `""` |
| `ConditionLevel` | enum? `Normal` / `Warning` / `Fault` / `Unavailable` | nazwa elementu w `Condition` |
| `NativeCode` | string? | `@nativeCode` dla CONDITION (kod alarmu, np. `AL-0123`) |

Paczka dla odbiorcy: `ObservationBatch(DeviceName, InstanceId, Source, Observations)` — jedna na urządzenie i odpowiedź, obserwacje posortowane po `Sequence`.

### Konfiguracja — `DataAcquisition:MTConnect` (Options, walidacja przy starcie)

| Klucz | Typ | Domyślnie | Zakres |
|---|---|---|---|
| `AgentUrl` | URI | `http://localhost:5000` | wymagany, absolutny `http`/`https` |
| `PollInterval` | TimeSpan | `00:00:01` | 100 ms – 1 h |
| `SampleCount` | int | `1000` | 1 – 1000 (zgodnie z limitem paczki API z DA-001) |
| `RequestTimeout` | TimeSpan | `00:00:10` | 1 s – 5 min |
| `RetryInitialDelay` | TimeSpan | `00:00:01` | 100 ms – `RetryMaxDelay` |
| `RetryMaxDelay` | TimeSpan | `00:00:30` | ≤ 1 h |

Wartości domyślne są w kodzie (klasa Options) — `appsettings.json` workera nie musi być zmieniany. Błąd walidacji → start przerwany, komunikat zawiera pełny klucz (np. `DataAcquisition:MTConnect:SampleCount`).

## Dostęp i interfejsy

- Agent MTConnect: tylko `GET`, bez uwierzytelnienia; `HttpClient` przez `IHttpClientFactory` (typed client) z `BaseAddress = AgentUrl` i `Timeout = RequestTimeout`.
- Brak nowych interfejsów zewnętrznych; worker nie nasłuchuje na żadnym porcie.
- Logi: bez treści odpowiedzi agenta i bez wartości obserwacji (tylko liczby, sekwencje, identyfikatory); poziomy: `Information` (start, synchronizacja, restart agenta, paczki), `Warning` (błędy przejściowe, luka po `OUT_OF_RANGE`, nieznany `dataItemId`).

## Kryteria odbioru

| AC | Przypadek testowy / Wejście | Oczekiwany wynik |
|---|---|---|
| AC-01 | `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` dla `DataAcquisition.slnx` | Kod 0 |
| AC-02 | Fixtures XML zebrane z symulatora (`/probe`, `/current`, `/sample`) + ręczne przypadki: CONDITION `Fault` z `nativeCode` i `Normal` bez tekstu, wartość `UNAVAILABLE`, pusty `/sample`, `MTConnectError OUT_OF_RANGE`, obcy namespace, uszkodzony XML | Pola modelu zgodne z tabelą; obserwacje posortowane po `sequence`; nagłówek odczytany; czas UTC; błędne dokumenty → rozpoznany błąd, nie wyjątek nieobsłużony |
| AC-03 | Fałszywy agent (sekwencja odpowiedzi) + `FakeTimeProvider`: probe → current (3 obs.) → sample (2) → sample pusty → sample (1); doganianie (`nextSequence ≤ lastSequence`) | Wywołania `/sample?from=` z kolejnymi `nextSequence`; odbiorca dostaje 3+2+1 obserwacji, każda raz, rosnąco; przy doganianiu brak czekania `PollInterval` |
| AC-04 | Fałszywy agent zmienia `instanceId` w odpowiedzi `/sample` | Log `Information`; ponowne `/probe` i `/current`; dalsze `/sample` z `nextSequence` nowej instancji |
| AC-05 | Fałszywy agent zwraca `OUT_OF_RANGE` | `/current` bez `/probe`; `Warning` z utraconym zakresem; pętla działa dalej |
| AC-06 | Fałszywy agent: wyjątek połączenia, timeout, HTTP 500, uszkodzony XML; potem poprawne odpowiedzi. Osobno: odbiorca rzuca wyjątek raz | `Warning`; opóźnienia 1 s, 2 s, 4 s… (max 30 s) mierzone `FakeTimeProvider`; po sukcesie powrót do 1 s; ten sam `from` po błędzie; po błędzie odbiorcy ta sama paczka dostarczona ponownie; host działa |
| AC-07 | Start z błędnym `AgentUrl` / `SampleCount = 0` / `PollInterval = 0`; `StopAsync` w trakcie oczekiwania i w trakcie żądania | Start przerwany `OptionsValidationException` z pełnym kluczem; zatrzymanie bez wyjątku i bez logu `Error`/`Warning` |
| AC-08 | Ręcznie: symulator `--seed 42` + worker ≥ 2 min; restart symulatora w trakcie | W logach odbiorcy ciągłe zakresy `sequence` (koniec paczki + 1 = początek następnej) w ramach instancji; po restarcie log zmiany `instanceId` i ciągłość w nowej instancji |

## Ryzyka i rozstrzygnięcia

- **At-least-once przy błędzie odbiorcy i przy migawce** — możliwe duplikaty po stronie odbiorcy; akceptowalne, bo API (DA-001) jest idempotentne po (`deviceId`, `instanceId`, `sequence`).
- **Utrata danych przy dłuższej niedostępności workera** — bufor agenta 131072 obserwacji (~ 17 min przy ~125 obs./s w symulatorze); po `OUT_OF_RANGE` luka jest logowana, nie odtwarzana. Brak trwałego zapamiętania sekwencji (poza zakresem).
- **Zależność od formatu symulatora** — parser oparty o MTConnect 1.3 i fixtures z symulatora; realny agent (np. cppagent 1.x/2.x) może mieć inne wersje namespace i dodatkowe atrybuty — obcy namespace jest odrzucany jawnie, a nie parsowany „na ślepo”. Obsługa innych wersji — osobny temat.
- **`NativeCode` nie ma odpowiednika w kontrakcie API** (DA-001) — przy DA-003 decyzja: rozszerzyć kontrakt albo dołączyć kod do `value`.
- **Pętla w `BackgroundService`** — wyjątek nieobsłużony zatrzymałby host (domyślne `BackgroundServiceExceptionBehavior.StopHost`); dlatego pętla łapie wszystkie wyjątki poza anulowaniem przy zatrzymaniu (AC-06/AC-07).
- **Zmiana istniejących testów workera** — 3 testy szkieletu (log „Worker running at”) zostaną zastąpione; tryb `feature`, więc dopuszczalne.
- **Alternatywa odrzucona**: strumień `multipart/x-mixed-replace` (`/sample?interval=`) — mniejsze opóźnienie, ale trudniejsza obsługa błędów i testy; odpytywanie co 1 s wystarcza na tym etapie.
