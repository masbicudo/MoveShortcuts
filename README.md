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

The goal is not to change how you use Windows — just to reduce friction.

---

## Notes

A few useful command-line options:

```text
MoveShortcuts --progress quiet
MoveShortcuts --progress log
MoveShortcuts --progress cli
MoveShortcuts --refresh-uwp-cache
```

Progress output adapts by default:

- Compact output in terminals  
- Log-style output when redirected  

You can always check available commands with:

```bash
MoveShortcuts --help
```

UWP / AppsFolder enumeration is cached in:

```bash
move-shortcuts-uwp-cache.json
```

Normal runs reuse this cache while the Windows package signature is unchanged.
Use `--refresh-uwp-cache` to rebuild it from AppsFolder.

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
