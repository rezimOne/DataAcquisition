# Pull Request: <ID — tytuł>

Status: draft. Gałąź: <gałąź>. Gałąź bazowa: <baza>. Autor: <osoba>. Źródła: [intent](intent.md), [plan](plan.md), [verification](verification.md), [review](review.md).

> Cykl statusu: draft → approved. Status approved nadaje wyłącznie człowiek po przeczytaniu `review.md` (hook blokuje agenta) — w tym samym commicie ustawia `Status: done` w `plan.md`. Push gałęzi przepuszcza dopiero `tools/Test-TopicReady.ps1` (hook git pre-push). Merge wykonuje człowiek na zdalnym repozytorium.

## Opis zmiany

<Co i dlaczego się zmienia — 2–4 zdania dla recenzenta. Odwołanie do problemu z intent.md.>

## Najważniejsze zmiany

- <zmiana 1 — plik / komponent>
- <zmiana 2>

## Ryzyka i wpływ

<Co może się zepsuć, kompatybilność wsteczna, migracje, konfiguracja, wydajność.>

## Jak testowano

<Komendy i wyniki — skrót z verification.md; czego nie przetestowano.>

## Uwagi dla recenzenta

<Miejsca wymagające szczególnej uwagi, świadome kompromisy, odstępstwa od planu.>

## Dane automatyczne

Sekcję generuje `tools/New-PullRequest.ps1` — nie edytuj ręcznie.

<!-- auto:start -->
_Uruchom `tools/New-PullRequest.ps1 -Id <ID>` lub `/sdlc-pr <ID>`._
<!-- auto:end -->

## Decyzja

- **Akceptacja do wypchnięcia**: <osoba, data — approved / changes-requested> — wpisuje człowiek po przeczytaniu `review.md`; razem z nią `plan.md` → `Status: done` (plan przestaje autoryzować edycję plików), a temat w `intent.md` / `BACKLOG.md` → `verified`.
- **Zdalny PR / merge**: <link lub numer PR na zdalnym repozytorium, data merge — uzupełnia człowiek>.
