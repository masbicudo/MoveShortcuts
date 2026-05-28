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

## Ownership Manifests

Managed output folders may contain both generated files and user-created files.
MoveShortcuts records generated files in ownership manifests and only overwrites
or removes files that it owns. If a file already exists but is not listed in the
manifest, it is treated as user-owned and left alone.

The user can explicitly include existing files in manifests with:

```text
MoveShortcuts manifest merge --auto-resolve include-user
```

That command lists the files first and asks for clearance before ownership is
changed.

The root shortcuts folder uses `move-shortcuts-manifest.json`. ProgramStarter's
timed startup folder uses `program-starter.json`.

## ProgramStarter

ProgramStarter is a small companion executable for delayed startup. MoveShortcuts
owns configuration, folder generation, and Windows Startup installation;
ProgramStarter only reads its folder and starts timed shortcuts when invoked
with `--start-now`.

Generated startup files use readable delay prefixes such as:

```text
45s_App.lnk
01m30s_Discord.lnk
```

Manual files with the same convention may be added to the ProgramStarter folder.
They are launched by ProgramStarter but are not overwritten or removed by
MoveShortcuts unless they become manifest-owned generated outputs.

ProgramStarter conflicts are checked by logical item name, not only by filename.
For example, `03m20s_Google Drive.lnk` conflicts with a generated
`02m50s_Google Drive.lnk` while the manual file remains user-owned.

Conflicts can be intentionally silenced with:

```text
MoveShortcuts manifest merge --auto-resolve ignore
```

Ignored conflicts are exact-match suppressions, not ownership decisions. If the
manifest path, current file path, or option target changes for that logical
identity, the ignore entry is cleared and the conflict is reported again.

For manifest-owned ProgramStarter files, MoveShortcuts treats delay changes as a
three-way merge:

- If the manifest delay and current filename still match, config may rename the
  owned file.
- If the current filename changed outside MoveShortcuts and config also changed,
  the entry is reported as a conflict.
- If a user-owned file has the same logical startup item name, generation is
  blocked until the user resolves or includes that file in the manifest.

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
