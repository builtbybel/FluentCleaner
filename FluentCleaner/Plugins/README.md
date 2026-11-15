# Plugins

Each folder here is one self-contained tool: engine, page and view models together, in its own
namespace (`FluentCleaner.Plugins.<Name>`). Nothing outside this folder references them except
two lines in `MainWindow.xaml.cs`, which is what puts them in the `+` menu:

```csharp
new(typeof(TweaksPage), "", "NavTweaks/Content", "Tweaks"),
new(typeof(AppsPage),   "", "NavApps/Content",   "Apps"),
```

Delete a folder and its line and the app still builds — that was verified, not assumed.

| Folder | What it does | Its data |
|---|---|---|
| `Tweaks/` | Registry settings catalogue: check, apply, reset | `WinRules.ini`, any further pack, `Lang\` |
| `Apps/` | Uninstaller for store apps and classic programs | `AppRules.ini` (classification only) |

Data files live **inside** the plugin that reads them, not in a shared store, and the build
copies them to the same relative path (`Plugins\Tweaks\WinRules.ini` beside the exe). One
folder is the whole plugin: its code, its pages, its rules and its translations. Deleting it
takes all of that with it, and nothing has to be excluded by name — the tweak catalogue reads
every `.ini` in its own folder precisely because `AppRules.ini` is not in there.

## Shared pieces

`Shared/` holds what both plugins need and nothing else does, in namespace
`FluentCleaner.Plugins` — the parent of each plugin's own namespace, so a plugin file reaches
these without a `using`. It deliberately sits here rather than in `Services\`: the core app
calls none of it, and putting it inside one plugin folder would make the other depend on that
plugin.

| File | What it is |
|---|---|
| `Loc.cs` | Localisation seam the ported engines call |
| `WorkProgress.cs` | One progress report shape for both command bars |
| `PluginPaths.cs` | Where a plugin's own data folder is at runtime |

Remove both plugin folders and this one goes with them; nothing else breaks. Nothing in
`Shared/` may reach into a plugin — that is why the translated rule wording sits in
`Tweaks/RuleText.cs` and not here: it reads a file out of the tweak plugin's own folder.

## Translating rules without touching code

Rule files keep their English wording, because translating them in place would fork the data
and turn every upstream catalogue update into a manual merge, per language. A language file
sits beside them instead and replaces only the wording:

```ini
; Plugins\Tweaks\Lang\de-DE.ini
[Disable File Explorer Ads]
Name=Explorer-Werbung abschalten
Detail=Schaltet die Hinweise von Synchronisierungsanbietern ab.
```

The heading is the rule's own name, so no ids are involved. `de-DE.ini` is tried first, then
`de.ini`. Both keys are optional, and anything left out keeps the English text — a partial
translation shows no gaps. See `Tweaks\Lang\_example.ini`; a leading underscore cannot be a
language code, so the template is never loaded.

## Adding rules without touching code

`Plugins\Tweaks\` beside the exe is the tweak catalogue's store. Dropping a `.ini` in there
installs a pack, deleting it uninstalls it, and a section name that already exists replaces
the earlier one — so a pack can correct an entry rather than only add to it. `Lang\` below it
is not read as a catalogue, because the search does not descend into subfolders.

## Writing a new plugin

1. New folder here, namespace `FluentCleaner.Plugins.<Name>`.
2. A `Page` implementing `ISearchablePage` / `IPageActions` from `FluentCleaner.Views` where it
   makes sense; the window supplies the search box, the `[...]` menu and the breadcrumb.
3. One line in `NewTabKinds`, plus `NavYourTool.Content` in `Strings\en-US` and `Strings\de-DE`.

Keep the engine free of UI types. Both current engines were ported from Bloatynosy precisely
because they were, which is what made the move a copy rather than a rewrite.
