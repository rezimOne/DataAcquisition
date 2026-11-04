# Praca z Kodem i Standardy Wytwarzania

Standardy deweloperskie, środowisko lokalne i polecenia weryfikacji kodu.

---

## 1. Wymagania środowiskowe

- **System operacyjny / Środowisko**: Windows 11 (deweloperskie); worker jest wieloplatformowy (.NET Generic Host).
- **SDK / Wersje runtime**: .NET SDK 10.0.x (zweryfikowano na 10.0.401), target `net10.0`; Python 3.9+ dla symulatora (bez zewnętrznych pakietów).
- **Narzędzia dodatkowe**: Git, PowerShell (narzędzia SDLC w `tools/` i hooki w `.claude/hooks/`).

---

## 2. Podstawowe polecenia

Wszystkie polecenia uruchamiaj z katalogu głównego repozytorium.

```bash
# 1. Pobranie zależności
dotnet restore DataAcquisition.slnx

# 2. Budowanie projektu
dotnet build DataAcquisition.slnx

# 3. Uruchomienie testów jednostkowych (xUnit v3, runner VSTest)
dotnet test DataAcquisition.slnx

# 4. Formatowanie i linter (weryfikacja; kod 0 = brak zmian)
dotnet format DataAcquisition.slnx --verify-no-changes
#    naprawa formatowania (tylko w plikach z zakresu planu!)
dotnet format DataAcquisition.slnx --include <ścieżka-pliku>
```

### Uruchomienie lokalne (symulator + worker)

```bash
# Terminal 1: symulator 5-osiowego CNC z agentem MTConnect na http://localhost:5000
py resources/cnc_mtconnect_sim.py --seed 42            # powtarzalny przebieg
py resources/cnc_mtconnect_sim.py --console --http-log # podgląd próbek i żądań HTTP

# Terminal 2: worker (profil Development z launchSettings.json)
dotnet run --project Worker/DataAcquisition.Worker.csproj
```

Szybki test agenta: `curl http://localhost:5000/probe`, `curl http://localhost:5000/current`.

### Testy

- Projekt: `Worker.Tests` (xUnit v3, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.Diagnostics.Testing`).
- Czas wstrzykujemy przez `TimeProvider` — w testach `FakeTimeProvider`, bez realnych `Task.Delay`/`Thread.Sleep` w logice.
- Logi weryfikujemy przez `FakeLogger<T>`; token anulowania testu: `TestContext.Current.CancellationToken`.
- Konwencja nazw: `Metoda_Warunek_OczekiwanyWynik`.

---

## 3. Konwencje i wytyczne implementacji

1. **Test-First dla błędów**: Każda naprawa błędu powinna rozpocząć się od testu jednostkowego lub integracyjnego, który reprodukuje problem (red), a następnie przejścia do kodu naprawczego (green).
2. **Czystość repozytorium**: Nie commituj plików tymczasowych, binariów (`bin/`, `obj/`, `node_modules/`, `.vs/`) ani konfiguracji zawierających hasła (`appsettings.Development.local.json`).
3. **Pojedyncza odpowiedzialność**: Zmiana w temacie powinna dotyczyć wyłącznie zakresu opisanego w `intent.md`. Unikaj „przy okazji” robienia niepowiązanych refaktoryzacji.
4. **Obsługa wyjątków**: Loguj wyjątki na odpowiednich poziomach (`Error` dla awarii systemu, `Warning` dla oczekiwanych sytuacji wyjątkowych). Nie tłum błędów pustymi blokami `catch`.
