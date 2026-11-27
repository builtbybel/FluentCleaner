# Writing a Winapp2 entry for FluentCleaner

Copy the template below, swap out the values, drop it into your `Winapp2.ini` and you're done.
The commented-out lines are optional — remove what you don't need.

---

```ini
; Entry name — shown in the UI. The trailing " *" marks it as a community entry,
; FluentCleaner strips it from the display automatically.
[My App Name *]

; ── Category ────────────────────────────────────────────────────────────────
; Pick one. LangSecRef is a numeric code (3021 = Apps, 3025 = Windows,
; 3026 = Internet). Section is a free-text fallback for anything custom.
LangSecRef=3021
; Section=My Custom Category


; ── Default selection ────────────────────────────────────────────────────────
; True  = checked by default (safe temp files, logs — the usual)
; False = unchecked by default (passwords, cookies, saved sessions — let the
;         user opt in consciously)
Default=True


; ── Warning tooltip ──────────────────────────────────────────────────────────
; Shown as a tooltip in the UI. Only add it when something could actually hurt.
; Skip it for plain *.tmp files.
; Warning=Removes saved login data. Make sure you know your passwords first.


; ── Detection ────────────────────────────────────────────────────────────────
; The entry only appears if at least one of these passes (OR logic).
; Detect    — registry key must exist (value doesn't matter)
; DetectFile — file or folder must exist on disk
; Mix them freely, any single hit is enough.
;
; For the big browsers there are named shortcuts so you don't need a manual key:
;   DET_CHROME  DET_FIREFOX  DET_EDGE  DET_OPERA  DET_THUNDERBIRD
Detect=HKCU\Software\MyApp
; Detect1=HKLM\Software\MyApp
DetectFile=%LocalAppData%\MyApp
; DetectFile1=%ProgramFiles%\MyApp\myapp.exe
; SpecialDetect=DET_CHROME


; ── Files to delete ──────────────────────────────────────────────────────────
; Format: FileKeyN=<path>|<pattern>[|RECURSE or REMOVESELF]
;
;   No flag    — top-level files only
;   RECURSE    — scan all subdirectories, leave empty folders behind
;   REMOVESELF — same as RECURSE but also prunes empty dirs when done
;
; Pattern supports wildcards (*.log) and multiple filters via semicolon (*.log;*.tmp).
; Leave out the pipe entirely → defaults to *.* (everything in that folder).
;
; Common vars: %AppData%  %LocalAppData%  %Temp%  %ProgramFiles%  %WinDir%

; top-level only, single filter
FileKey1=%LocalAppData%\MyApp\Logs|*.log

; multiple filters in one key
FileKey2=%LocalAppData%\MyApp\Cache|*.tmp;*.bak

; recurse into subdirs, keep folder structure
FileKey3=%LocalAppData%\MyApp\Temp|*.*|RECURSE

; recurse and remove empty dirs afterwards
FileKey4=%LocalAppData%\MyApp\OldCache|REMOVESELF

; wildcard in the path itself — hits every Firefox profile folder
; FileKey5=%AppData%\Mozilla\Firefox\Profiles\*\cache2\entries|*.*|REMOVESELF


; ── Registry keys to delete ──────────────────────────────────────────────────
; Format: RegKeyN=<HIVE\SubKey>[|ValueName]
;
; Without ValueName — entire key tree is deleted
; With ValueName    — only that one value is removed, the key itself stays
;
; Hives: HKCU  HKLM  HKU  HKCC  HKCR

; delete entire key
RegKey1=HKCU\Software\MyApp\RecentFiles

; delete only a single value, leave the key intact
RegKey2=HKCU\Software\MyApp\Settings|LastOpenedFile


; ── Exclusions ───────────────────────────────────────────────────────────────
; Protect files or folders even if a FileKey above would match them.
;
; FILE|<path>\|<name or pattern>  — protects a specific file or pattern inside a folder
;                                   exact name: places.sqlite
;                                   wildcard:   *.db   keep_*.txt
; PATH|<path>\                    — protects the entire directory subtree
;
; The path must end with a backslash.

; protect one specific file
ExcludeKey1=FILE|%AppData%\MyApp\|important.db

; protect all files matching a pattern
ExcludeKey2=FILE|%LocalAppData%\MyApp\Cache\|*.sqlite

; protect an entire subfolder
ExcludeKey3=PATH|%LocalAppData%\MyApp\DoNotTouch\
```

---

### Minimal real-world example

No optional fields, just the bare minimum that actually works:

```ini
[My App Temp Files *]
LangSecRef=3021
DetectFile=%LocalAppData%\MyApp
FileKey1=%LocalAppData%\MyApp\Cache|*.tmp|RECURSE
```

That's it. App detected → cache cleaned. Everything else is opt-in.
