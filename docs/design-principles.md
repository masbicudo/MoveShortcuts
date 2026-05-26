# Move Shortcuts Design Principles

This project turns desktop, Start Menu, UWP, URL, file, and folder entries into a personal command surface, usually through a shortcuts directory such as `C:\Shortcuts` that is added to `PATH`.

## PATH Safety

The root shortcuts directory is a PATH overlay, not a replacement for vendor-provided commands.

Before creating a root command such as `ollama.lnk`, `code.cmd`, or `cdx.ps1`, the tool checks whether the same command name already resolves to something outside the shortcuts directory. If it does, the tool skips that generated command.

It is safe to overwrite or regenerate commands that already live inside the shortcuts directory, because those are considered managed outputs.

## Vendor Shortcuts

Prefer vendor-provided `.lnk` and `.url` files when available. They often contain arguments, working directories, icons, app-user model IDs, or launch behavior that is better than reconstructing a shortcut from an executable path.

When a vendor shortcut name conflicts with an external command, keep useful aliases and skip only the conflicting name. For example, if `ollama.exe` exists outside `C:\Shortcuts`, do not create `Ollama.lnk`, but still allow aliases such as `ollama-app.lnk` and `ollama-gui.lnk`.

## Aliases

Aliases are conveniences, but they must obey the same PATH safety rule as primary names. An alias should never shadow a vendor command or another external executable.

## Groups

Group folders such as `IA`, `Office`, `Games`, and `Cleaning` are organizational mirrors. They are not intended to be added to `PATH`.

Because group folders are not PATH surfaces, group copies do not need the external-command shadow check. If a root shortcut is safely created, its group copy should be copied normally.

## Config Location

The active configuration is read from the current working directory as `move-shortcuts-options.json`.

For the current Debug build, the intended working directory is:

```text
{workspace}/MoveShortcuts/bin/Debug/net10.0-windows
```

Running the executable from another directory may create or read a different options file, which can be confusing.

## Current Output Target

The intended local build output is:

```text
{workspace}/MoveShortcuts/bin/Debug/net10.0-windows
```

Avoid keeping stale `bin` or `obj` target directories around unless actively testing another target framework.
