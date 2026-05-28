# Move Shortcuts

Move Shortcuts exists for a simple reason: shortcut clutter gets out of hand fast.

Installers drop icons everywhere — Desktop, Start Menu, hidden folders — and over time things become harder to find than they should be. This tool takes a more intentional approach: gather shortcuts, organize them in one place, and optionally make them easier to launch.

Think of it as a way to “reset” shortcut chaos into something clean and predictable.

---

## Quick start

Start by creating a configuration:

```text
MoveShortcuts init
```

This step is interactive. It asks what should be included (Desktop, Start Menu, UWP apps), whether alias shortcuts should be created, and whether Desktop shortcuts should be removed after everything is organized.

Nothing is changed immediately — a config file is written first:

```bash
move-shortcuts-options.json
```

You can review it before doing anything else.

Once you're happy with the configuration, run:

```bash
MoveShortcuts
```

---

## What it does

By default, everything ends up in a single folder:

```bash
C:\Shortcuts
```

This can be changed in the configuration file.

From there, the tool works through your chosen shortcut sources:

- Desktop shortcuts  
- Start Menu shortcuts  
- UWP / AppsFolder programs  

It can also:

- Clean up processed Desktop shortcuts  
- Create shorter alias shortcuts based on initials (useful for quick launching)  
- Generate delayed startup shortcuts through the small ProgramStarter companion  

The goal is not to change how you use Windows — just to reduce friction.

---

## Notes

A few useful command-line options:

```text
MoveShortcuts --progress quiet
MoveShortcuts --progress log
MoveShortcuts --progress cli
MoveShortcuts --refresh-uwp-cache
MoveShortcuts --add-user-path
MoveShortcuts --add-user-path first
MoveShortcuts --add-machine-path
MoveShortcuts edit
MoveShortcuts startup status
MoveShortcuts startup run
MoveShortcuts manifest merge --auto-resolve include-user
MoveShortcuts manifest merge --auto-resolve ignore
```

Progress output adapts by default:

- Compact output in terminals  
- Log-style output when redirected  

You can always check available commands with:

```bash
MoveShortcuts --help
```

`MoveShortcuts edit` opens the active `move-shortcuts-options.json` with your
default editor, falling back to Notepad if needed.

ProgramStarter is an optional companion runner for delayed logon startup. When
enabled in config, MoveShortcuts creates one Windows Startup item and writes
timed shortcuts into:

```bash
C:\Shortcuts\ProgramStarter
```

Managed files use readable names such as `01m30s_Discord.lnk`. You can also add
manual timed files there; MoveShortcuts tracks its own generated files in
manifests and leaves unowned files alone.

If you want MoveShortcuts to take ownership of files that already exist in a
managed folder, run `MoveShortcuts manifest merge --auto-resolve include-user`.
It lists the files first and asks for clearance before adding them to the
manifests.

For conflicts you intentionally want to leave unresolved, `MoveShortcuts
manifest merge --auto-resolve ignore` suppresses the exact current conflict. If
the manifest, file name, or option changes, the ignore is invalidated and the
conflict is reported again.

During `init`, MoveShortcuts can also add your shortcuts folder to PATH if it is
not already there. User PATH is updated directly; machine PATH uses a one-time
UAC prompt when you choose that option.

PATH setup is conservative by default: if the shortcuts folder is already in
PATH, its position is left alone. New entries are appended to the end. For
explicit control, use `first` to move it to the front or `last` to move it to
the end.

UWP / AppsFolder enumeration is cached in:

```bash
move-shortcuts-uwp-cache.json
```

Normal runs reuse this cache while the Windows package signature is unchanged.
Use `--refresh-uwp-cache` to rebuild it from AppsFolder.

Dynamic targets such as `es tool.exe` and `where tool.exe` are cached in:

```bash
move-shortcuts-target-cache.json
```

The cached target is reused only while the resolved file or folder still exists.

---

## Design principles

If you're curious about the internal decisions behind how this works, see:

[Design Principles](docs/design-principles.md)

It covers things like:

- PATH safety rules  
- Vendor shortcut handling  
- Alias generation rules  
- Folder grouping behavior  
- Debug and config locations

For details on the UWP cache design, see:

[UWP Cache Design](docs/uwp-cache-design.md)

There is also a short performance notebook:

[Performance Optimizations](docs/optimizations.md)

The project has a small `research/` folder documenting the UWP/AppModel
investigation behind the cache. It is not required to use the tool, but it shows
the kind of care taken around Windows-specific behavior and performance.

## Closing note

This tool is intentionally small in scope.

It doesn’t try to replace Windows behavior — it just makes shortcut management feel less messy and more deliberate.

## License

This project is licensed under the Apache License, Version 2.0. See the [LICENSE](LICENSE) file for details.
