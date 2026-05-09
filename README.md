# FluentCleaner
### modern, transparent, no spyware, no scareware, no dark patterns, no upsell garbage, no fake registry magic


<img width="1536" height="1024" alt="FluentCleaner" src="FluentCleaner/Assets/Banner.avif" />


_i built my own take on a cleaner, inspired by the old CCleaner from back in the 2006 days, just adapted to how things should work today. modern (built with WinUI 3), minimal and focused on actually cleaning what matters (without all the usual nonsense)_
 
i built this because at some point you start noticing a pattern

things that were genuinely good… slowly become worse.
small devs ship something great, a company buys it, optimizes it into oblivion, and suddenly you're left wondering how a simple tool turned into a "what happened here?" story. CCleaner is basically a case study at this point, everyone knows, nobody needs another paragraph about it

funny enough, CCleaner only ever really survived because of the community around it, especially things like the [winapp2.ini](https://github.com/moscadotto/winapp2) signatures. that ecosystem did more for the tool than most official decisions ever did.

i was too lazy to rebuild all cleaners natively, so i just wrote a parser for that format instead. turns out its fast. like… surprisingly fast. faster than what i remember from the old piriform implementation (no idea why that was so slow, proprietary formats, overengineering, or just history doing its thing. doesnt matter anymore anyway)

the UI is built in WinUI3 you know, Microsofts "beautiful but slow" framework, except somehow it still manages to outperform the original. go figure

companies today dont really compete on making things better. they compete on who can add more noise without breaking everything completely. and somewhere along the way, "good tools" just turned into "things people remember fondly"

CCleaner used to be great. now it’s mostly a warning.

anyway, im not trying to fix the indutry. just wanted something that doesnt suck. i'll probably get bored, or it'll evolve into something else, and we end up back at square one like always.

for now i just called it **FluentCleaner**.

it wasnt even meant to be public, but a lot of genuinely nice people asked me to release it, so i probably will
here's a first preview so you can get a feel for the direction. i might end up funding it through donations, we'll see.

if you like it, cool. if not, also fair

## FAQ

<details>
<summary>will this make my PC faster?</summary>

honestly? it depends and that's not a cop-out

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

</details>

<details>
<summary>is it safe?</summary>

it's as safe as what you enable. nothing runs without you selecting it first.
winapp2.ini entries only target what they're explicitly told to target,
no broad "delete everything in temp" nonsense.
that said: it deletes files. take a backup if something feels important

</details>

<details>
<summary>why WinUI 3?</summary>

because it's 2026 and windows tools shouldn't look like they were built in 2009.
also fluent design is literally in the name. felt right

</details>

<details>
<summary>CCleaner 7 dropped winapp2.ini support, what does that mean for FluentCleaner?</summary>

nothing. FluentCleaner has its own parser, completely independent of CCleaner
CCleaner dropping support was honestly part of the motivation to build this

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

## faq — "why doesn't FluentCleaner have X?"

---

### secure file deletion (dod 7-pass, gutmann 35-pass…)

short answer: it would look impressive and do nothing useful.
secure overwrite made sense in the 90s when hdds were standard and forensic recovery was a real concern. today:

- **ssds** use wear leveling and trim. the controller decides where bits physically land — not your software. you can overwrite a file 35 times and the controller writes to different nand blocks anyway. gutmann himself noted this in an addendum to his own paper.
- **the files fluentcleaner deletes** are browser cache, temp files and log entries. if someone is forensically recovering your discord cache you have bigger problems than your cleaner's deletion method.

normal file deletion is correct here. anything else is security theater.

---

### registry cleaner

deliberate omission, worth explaining.

the premise sounds reasonable, orphaned keys accumulate, windows slows down, cleaning helps. in practice:

- windows loads registry keys on demand. ten thousand orphaned uninstaller entries have zero measurable impact on boot time or performance. this has been benchmarked to death.
- the risk/reward is completely inverted. a registry cleaner that removes the wrong key can break applications or in edge cases the os itself. the upside is placebo. the downside is a broken install.

ccleaner has one because it's a selling point that *sounds* technical. fluentcleaner doesn't have one because shipping a feature that exists to look good rather than do good would be dishonest.

if you actually need to clean up after a broken uninstaller — [autoruns](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns) or a targeted manual edit is the right tool, not a bulk cleaner.

---

### general philosophy

FluentCleaner targets things that are unambiguously junk; cache files, temp data, leftover logs. it deliberately avoids the feature creep that turned ccleaner from a focused utility into bloatware with a vpn upsell on every launch.

fewer features. honest features.
