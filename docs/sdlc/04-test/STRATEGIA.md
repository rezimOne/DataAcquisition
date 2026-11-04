# Strategia Testów i Weryfikacji Jakości

Wytyczne dotyczące poziomów testowania, środowisk testowych oraz standardu gromadzenia dowodów jakości.

---

## 1. Poziomy testów (Piramida Testów)

| Poziom testu | Zakres i cel | Narzędzia / Środowisko | Wymagany dowód |
|---|---|---|---|
| **Jednostkowe (Unit)** | Pojedyncze metody, reguły biznesowe, walidacja | Framework testowy (np. xUnit, Jest, pytest) | Wynik wykonania polecenia testowego w konsoli |
| **Integracyjne (Integration)** | Komunikacja z bazą danych, migracje, serwisy | Izolowana baza testowa / Testcontainers | Raport z wykonania testów integracyjnych |
| **API / Kontrakty** | Kody HTTP (200, 400, 401, 404, 500), payloady JSON | Testy HTTP / Postman / curl / REST Client | Zrzut odpowiedzi API z nagłówkami i statusem |
| **End-to-End / Dymne** | Krytyczne ścieżki użytkownika | Testy manualne lub automatyczne (Playwright/Selenium) | Lista sprawdzonych kroków i zrzut ekranu |

---

## 2. Standard dowodu weryfikacji (`verification.md`)

Deklaracja słowna „sprawdzone i działa” jest **niedopuszczalna**. Prawidłowy dowód wymaga:
- Dokładnego wskazania commita (`SHA`).
- Wskazania profilu środowiska (np. `Test/Staging`, wersja bazy).
- Konkretnego wycinka logu konsoli z kodem wyjścia `0` lub zrzutu odpowiedzi HTTP.
- Jawnego wskazania ograniczeń: jeśli dany warunek `AC` nie mógł zostać sprawdzony automatycznie, należy to wprost wpisać w sekcji *Pominięcia i ograniczenia*.
