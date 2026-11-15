# FluentCleaner 26.11.03 — The Plugin Update

I'm getting slower. I'd like to blame the framework, but I think it's just me.

There was a time I'd ship three tools in a weekend. These days I open an editor, stare
at a `Grid.ColumnDefinitions` for twenty minutes, and go make coffee. And at some point
while maintaining Bloatynosy over here and FluentCleaner over there and that other thing
I keep forgetting the name of, it occurred to me that I wasn't maintaining several tools.
I was maintaining several copies of myself, and all of them were tired.

So I stopped. I took the tweaking and debloating engines out of my other tools and folded
them into the WinUI 3 FluentCleaner — not bolted on, but as **plugins**: self-contained
folders that dock into the app and can be ripped back out without leaving a scar. I
actually tested that. Deleted both folders, hit build, and it compiled green. Then I put
them back, because I'd like to keep them.

This might be the only app I keep developing. One window, one codebase, one place where
the thing you need is already a tab away. We'll see. Ask me again in two months.

---

## Added

- **[Added]** Tweaks plugin — a catalogue of 98 Windows registry settings that checks what's
  already set on your PC, applies what you pick, and puts any of it back.
- **[Added]** Apps plugin — uninstaller for Store packages and classic programs, with a safety
  rating so you can see what Windows actually needs before you tick it.
- **[Added]** Plugin architecture: each tool is one folder under `Plugins\`, carrying its own
  engine, pages and data. Adding one to the `+` menu is two lines.
- **[Added]** Rule packs without a rebuild — drop an `.ini` into `Plugins\Tweaks\`, and a
  section that already exists replaces the earlier one, so a pack can correct a rule.
- **[Added]** Rule translations as plain files — `Plugins\Tweaks\Lang\de-DE.ini` ships complete,
  all 98 rules. A new language is one file, no code, no build.
- **[Added]** Right-click on any tweak or app row: apply, reset, copy the registry path, open
  it in Regedit, or uninstall just that one.
- **[Added]** A **Status** column that says what's already applied on your machine and what's
  still open, with a tick and a colour instead of making you read the fine print.
- **[Added]** Filters that make a 94-row catalogue usable: by category, and "only open".
- **[Added]** The description of the selected rule, with every registry value it writes, in a
  strip under the list. This app writes to your registry; "trust me" isn't good enough.

## Improved

- **[Improved]** Startup is faster. The cleaner catalogue loads in ~145 ms instead of ~325 ms,
  because the "is this installed?" check now runs across cores and stops asking the same
  path twice — 4775 detection lines in the catalogue, only 3408 of them distinct.
- **[Improved]** The window comes up before the first page is built, instead of after it.
- **[Improved]** The status bar now tells you what the numbers mean: `175 of 4076 entries`,
  so you can see how much of the catalogue applies to your machine.
- **[Improved]** Tick boxes in the tweak list stopped fighting the row highlight. The box
  ticks, everything else selects, and both finally look like they meant to.
- **[Improved]** Right-clicking a row works across the whole row, not only where there's text.
- **[Improved]** Row menus are built once per page instead of once per row.
- **[Improved]** Tidier source tree: plugin data lives inside the plugin that reads it, and
  shared helpers sit in `Plugins\Shared\` rather than pretending to belong to the core app.

## Removed

- **[Removed]** The terminal's `appx` debloater and its `Winappx.ini` database, along with the
  settings row that downloaded it. The Apps plugin does the same job from a list you can
  actually see — and two debloaters that disagreed about the same question was one too many.
- **[Removed]** The category count from the status bar. The tree next to it already shows them.

## Fixed

- **[Fixed]** "Check again" in Tweaks did nothing on the second run and left the buttons
  greyed out for good.
- **[Fixed]** Dialogs rendered light while the app was dark.
- **[Fixed]** A stream of first-chance `NamedResource` exceptions behind the tweak scanner.
- **[Fixed]** "Uninstall 1 apps?" is now a sentence.

---

## Known rough edges

- The Tweaks and Apps pages are translated in German and English only. Other languages fall
  back to English on those two pages.
- Applying a tweak needs administrator rights for anything under the machine hive; the list
  marks those rows with a lock before you run, not after.
