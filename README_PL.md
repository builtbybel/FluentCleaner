[<a href="README.md">English</a>]| [<a href="README-PL.md">Polski</a>]

> [!UWAGA]
> ## ⚠️ Ostrzegam przed fałszywymi stronami internetowymi FluentCleaner
>
> **FluentCleaner nie ma żadnego związku ze stroną `fluentcleaner.org`**
>
> **Jedynym oficjalnym źródłem** programu FluentCleaner jest **to repozytorium GitHub**. Nie jestem właścicielem, nie zarządm tą stroną, ani żadną inną stroną pobierania.
>
> **Dla Twojego bezpieczeństwa - pobieraj FluentCleaner tylko z oficjalnych kompilacji dostępnych tutaj, na GitHub.**
>
> Ładnie wyglądająca strona nie zawsze jest oficjalną, **zawsze sprawdzaj źródło. 🛡️**
---
# FluentCleaner
### nowoczesny, przejrzysty, bez programów szpiegujących, bez oszustwa, bez zaciemniania kodu, bez śmieciowych dodatków, bez fałszywych sztuczek z rejestrem


<img width="1536" height="1024" alt="FluentCleaner" src="FluentCleaner/Assets/Banner.png" />


_i built my own take on a cleaner, inspired by the old CCleaner from back in the 2006 days, just adapted to how things should work today. modern (built with WinUI 3), minimal and focused on actually cleaning what matters (without all the usual nonsense)_
 
i built this because at some point you start noticing a pattern

things that were genuinely good… slowly become worse.
small devs ship something great, a company buys it, optimizes it into oblivion, and suddenly you're left wondering how a simple tool turned into a "what happened here?" story. Ccleaner is basically a case study at this point, everyone knows, nobody needs another paragraph about it

funny enough, CrapCleaner only ever really survived because of the community around it, especially things like the [winapp2.ini](https://github.com/moscadotto/winapp2) signatures. that ecosystem did more for the tool than most official decisions ever did.

i was too lazy to rebuild all cleaners natively, so i just wrote a parser for that format instead. turns out its fast. like… surprisingly fast. faster than what i remember from the old piriform implementation (no idea why that was so slow, proprietary formats, overengineering, or just history doing its thing. doesnt matter anymore anyway)

the UI is built in WinUI3 you know, Microsofts "beautiful but slow" framework, except somehow it still manages to outperform the original. go figure

companies today dont really compete on making things better. they compete on who can add more noise without breaking everything completely. and somewhere along the way, "good tools" just turned into "things people remember fondly"

CCleaner used to be great. now it’s mostly a warning.

anyway, im not trying to fix the indutry. just wanted something that doesnt suck. i'll probably get bored, or it'll evolve into something else, and we end up back at square one like always.

for now i just called it **FluentCleaner**.

it wasnt even meant to be public, but a lot of genuinely nice people asked me to release it, so i probably will
here's a first preview so you can get a feel for the direction. i might end up funding it through donations, we'll see.

if you like it, cool. if not, also fair

## 🚀 Pobierz najnowszą wersję stabilną
FluentCleaner dostępny jest w dwóch wariantach. Ten sam silnik czyszczączy oraz parser pliku winapp2.ini, różne interfejsy użytkownika i sposób uruchamiania.

|  | **FluentCleaner** (WinUI 3) | **FluentCleaner Classic** |
|---|---|---|
| Framework | .NET 10 + Windows App SDK | .NET Framework 4.8 (WinForms) |
| Wdrożenie | samodzielnie (dołączone wymagane biblioteki) | zależne od bibliotek (korzysta ze środowiska obecnego w systemie) |
| Rozpakowana wielkość | ~140 MB | ~3.57 MB |
| Plików | 243 | 20 |
| Platforma | x64 / ARM64, Windows 10 build 17763+ | praktycznie każdy Windows |
| Wspólny silnik | `FluentCleaner.Core` (netstandard2.0) — logika skanowania/czyszczenia, parser Winapp2.ini | to samo |
| Wymagania | Windows 10 2004 (Build 19041) lub nowszy + [runtime Windows App SDK 2.0.1](https://aka.ms/windowsappsdk/2.0/2.0.1/windowsappruntimeinstall-x64.exe) (instalowane tylko raz, samodzielnie) | brak - używa dowolnego .NET Framework zainstalowanego w Twoim systemie |
| Pobieranie | [⬇ Najnowszy](https://github.com/builtbybel/FluentCleaner/releases/latest/download/FluentCleaner-win-x64.zip) | [⬇ Najnowszy](https://github.com/builtbybel/FluentCleaner/releases/latest/download/FluentCleaner-Classic-net48.zip) · [więcej informacji](https://github.com/builtbybel/FluentCleaner/releases/tag/classic-1.0.0) |

**Nie wiesz, który pobrać?** If you want the modern look and don't mind installing the Windows App SDK once, go with the main version. If you want something tiny, portable, and framework-dependent (or you're on an older/locked-down machine), grab Classic.

### Instalacja za pomocą WinGet

Nowoczesna **edycja WinUI 3** jest także dostępna za pomocą Managera Pakietów Windows:

```powershell
winget install --id Builtbybel.FluentCleaner --exact
```

Już zainstalowany? Zaktualizuj do najnowszej wersji:
```powershell
winget upgrade --id Builtbybel.FluentCleaner --exact
```
_WinGet aktualnie instaluje nowoczesną edycję. FluentCleaner Classic pozostaje dostępny jako przenośna wersja._

> 💬 **Klasyczny czy Nowoczesny?** [Głosuj tutaj](https://github.com/builtbybel/FluentCleaner/discussions/100) — ciekawe, której wersji ludzie używają na codzień.

## ❤️ Wsparcie

no company behind this, no investors, no marketing department. just one person building a tool because the alternative got worse every year. every bit of support helps keep it going

| PayPal | Ko-fi |
|---|---|
| [Wesprzyj z PayPal](https://www.paypal.com/donate/?hosted_button_id=99X8UQJQP96WN) | [Wesprzyj z Ko-fi](https://ko-fi.com/builtbybel) |

Skromne początki, podobnie jak kiedyś CCleaner.

## FAQ

<details>
<summary>Czy to przyspieszy mój PC?</summary>

Szczerze? To zależy - i to nie jest wymówka..

on a modern system with plenty of free space, you probably won't notice a dramatic speed boost.
but Microsoft themselves say that running low on storage can slow things down and even block 
Windows updates ([source](https://support.microsoft.com/en-us/windows/free-up-drive-space-in-windows-85529ccb-c365-490d-b548-831022bc9b32)) so if your drive is getting full, cleaning matters more than you'd think.

beyond speed, there are solid reasons to clean regularly:
- reclaim disk space that's been quietly eaten up over months
- troubleshoot app issues caused by corrupted cache
- shrink backup size
- privacy, browser data, recently opened file lists, leftover traces from apps you uninstalled
- keep Windows updates running smoothly
- or just because a tidy system feels better. also valid.

Microsoft recommends doing this monthly. Storage Sense does it automatically.
FluentCleaner just gives you more control over what exactly gets cleaned

</details>

<details>
<summary>FluentCleaner (Modern/WinUI version) shows "Unknown Hard Error" or won't launch</summary>

the modern version requires the exact [Windows App SDK 2.0.1 x64 runtime](https://aka.ms/windowsappsdk/2.0/2.0.1/windowsappruntimeinstall-x64.exe).
an older or newer Windows App SDK runtime will not work.

after installing it, restart Windows so the runtime can register properly.
if the problem continues, add the exact error and your Windows version to [issue #85](https://github.com/builtbybel/FluentCleaner/issues/85).

</details>

<details>
<summary>what even is winapp2.ini?</summary>

a community-maintained database of cleaning rules for Windows apps,
thousands of entries built up over 15+ years. it tells FluentCleaner exactly
what to clean for each app: which temp folders, which cache paths, which registry keys.
no guessing, no sweeping wildcards across your whole drive.
every entry is specific, inspectable, and auditable. that's the whole point.

</details>

<details>

<summary>what are flavors?</summary>

winapp2.ini comes in different variants depending on which tool you're using.
FluentCleaner uses the original CCleaner flavor, the same one that powered
the tool back when it was still worth using

</details>

<details>
<summary>is it safe?</summary>

it's as safe as what you enable. nothing runs without you selecting it first.
winapp2.ini entries only target what they're explicitly told to target,
no broad "delete everything in temp" nonsense.
that said: it deletes files. take a backup if something feels important

</details>

<details>
<summary>Dlaczego WinUI 3?</summary>

because it's 2026 and windows tools shouldn't look like they were built in 2009.
also fluent design is literally in the name. felt right

</details>

<details>
<summary>CCleaner 7 dropped winapp2.ini support, what does that mean for FluentCleaner?</summary>

nothing. FluentCleaner has its own parser, completely independent of CCleaner
CCleaner dropping support was honestly part of the motivation to build this

</details>

<details>

<summary>Czy mogę przedłumaczyć FluentCleaner na mój język?</summary>

Tak, proszę 🙌 tak został zaprojektowany. Oto cały proces:

1. skopiuj `FluentCleaner/Strings/en-US/Resources.resw`
2. utwórz `FluentCleaner/Strings/{twój-język}/Resources.resw` (no. `fr-FR`, `pt-BR`, `zh-CN`)
3. przetłumacz każdą wartość `<value>` — **pozostaw nietknięte nazwy kluczy (`name="..."`)**
4. ustaw `LblTranslatorCredit` na swoje dane (dodaj swoją stronę, jeżeli chcesz widnieć na liście)
5. otwórz `pull request` 🎉

two things that trip people up:
- **don't touch the XML structure** — no editing `<data>`, `<resheader>`, the version header or the `<?xml ...>` line. only the text inside `<value>` gets translated.
- **this is only for the app UI.** the cleaning databases (winapp2.ini etc.) come from the upstream Winapp2 project and aren't translated here.

save the file as UTF-8 and you're good. don't see your language yet? that just means nobody's done it — could be you 😉

</details>

<details>
<summary>Czy mogę przełumaczyć FluentCleaner Classic na mój język?</summary>

Absolutnie 🙌 Classic wczytuje tłumaczenia z oddzielnych plików JSON w czasie pracy, więc niepotrzebna jest rekompilacja programu.

1. skopiuj `FluentCleaner.Classic/Localization/en.json`
2. zmień nazwę na swoją lokalną (np. `fr-FR.json`, `pt-BR.json`, `ja-JP.json`)
3. przetłumacz wartości tekstowe — **zachowaj wszystkie klucze i symbole zastępcze jak `{0}` bez zmian**
4. wypełnić wartości `_Meta_LanguageDisplayName`, `_Meta_TranslatorName` i opcjonalnie `_Meta_TranslatorWebsite`
5. przetestuj, wybieracjąc **Options > Settings > Open localization folder**
6. prześlij plik JSON za pomocą `pull request` lub tworząc zgłoszenie na GitHub 🎉.

Zobacz pełny [Przewodnik tłumaczenia wersji Classic](FluentCleaner.Classic/TRANSLATING_CLASSIC_PL.md), aby otrzymać więcej szczegółów.

</details>

<details>
<summary>can i use a custom winapp2 database?</summary>

yes. FluentCleaner isn't locked to one source.

tools like BBleachBit (primarily a Linux cleaner, discovered it through this project actually,
but the UI was bad enough that it came right back off), and others have their own flavors of winapp2.ini,
slightly modified versions tuned for their specific needs. you can grab any of them
(or build your own) and plug it straight into FluentCleaner.

just drop the file somewhere on your system, then head to:
**Settings > Database > Custom** and point it at your file. that's it.

the official database from the winapp2 project lives here:
https://github.com/MoscaDotTo/Winapp2, it's community-maintained, 
updated regularly, and covers thousands of apps. a solid starting point
if you want more coverage than the default.

</details>

<details>
<summary>where can i follow development?</summary>

i post insider stuff, early builds and the occasional rant about winui on **[x/twitter](https://x.com/builtbybel)**. if you want to know what's coming before it lands in a release, that's the place.

issues and feature requests go here on github as usual.

</details>

<a id="task-scheduler"></a>
<details>
<summary>Can I run FluentCleaner without a UI / from Task Scheduler?</summary>
yes.

```powershell
FluentCleaner.exe /AUTO
```

Runs a silent cleanup using your currently saved selection and exits immediately.  
No window, prompts or interaction.

```powershell
FluentCleaner.exe /AUTO /SHUTDOWN
```

Same behavior, but shuts Windows down after cleanup finishes.  
`/SHUTDOWN` alone does nothing.

### Dziennik zdarzeń

Each automatic run appends a detailed log to:

```txt
%AppData%\FluentCleaner\auto.log
```

The log contains:
- timestamp
- every deleted path grouped by entry
- total cleaned size

### Harmonogram

To automate cleanup:

1. Open **Windows Task Scheduler**
2. Create a new task
3. Add `FluentCleaner.exe`
4. Use `/AUTO` as argument

No built-in scheduler UI needed.

</details>

<details>
<summary>Jakie wersje Windows są wspierane?</summary>

FluentCleaner oficjalnie wspiera:

- Windows 10 2004 (Build 19041) i późniejsze
- Windows 11

Nie jest wymagany Windows 11.
Despite using WinUI 3, the app is intentionally built to remain compatible with modern Windows 10 systems as well

</details>

<details>
<summary>Czy mogę wspomóc pisanie programu?</summary>

Tak, jeśli masz ochotę 😄

FluentCleaner is a one-person project, not a multi-million dollar company with investors and a marketing department.

If you want to support development financially, you can do so here:
[PayPal](https://www.paypal.com/donate/?hosted_button_id=99X8UQJQP96WN)


</details>

 ## Mity optymalizacji
 
<details>
<summary>Dlaczego FluentCleaner nie posiada opcji X?</summary>

<details>
<summary>secure file deletion (dod 7-pass, gutmann 35-pass…)</summary>

short answer: it would look impressive and do nothing useful.

secure overwrite made sense in the 90s when hdds were standard and forensic recovery was a real concern. today:

- **ssds** use wear leveling and trim. the controller decides where bits physically land,not your software. you can overwrite a file 35 times and the controller writes to different nand blocks anyway. gutmann himself noted this in an addendum to his own paper.
- **the files Fluentcleaner deletes** are browser cache, temp files and log entries. if someone is forensically recovering your discord cache you have bigger problems than your cleaner's deletion method.

normal file deletion is correct here. anything else is security theater.

</details>

<details>
<summary>registry cleaner</summary>

deliberate omission, worth explaining.

the premise sounds reasonable ; orphaned keys accumulate, windows slows down, cleaning helps. in practice:

- windows loads registry keys on demand. ten thousand orphaned uninstaller entries have zero measurable impact on boot time or performance. this has been benchmarked to death.
- the risk/reward is completely inverted. a registry cleaner that removes the wrong key can break applications or in edge cases the os itself. the upside is placebo. the downside is a broken install.

ccleaner has one because it's a selling point that *sounds* technical. FluentCleaner doesn't have one because shipping a feature that exists to look good rather than do good would be dishonest.

if you actually need to clean up after a broken uninstaller;[autoruns](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns) or a targeted manual edit is the right tool, not a bulk cleaner.

</details>


<details>
<summary>Ogólna filozofia</summary>

FluentCleaner targets things that are unambiguously junk;cache files, temp data, leftover logs. it deliberately avoids the feature creep that turned ccleaner from a focused utility into bloatware with a vpn upsell on every launch.



fewer features. honest features.

</details>

</details>
