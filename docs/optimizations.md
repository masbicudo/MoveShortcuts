# Performance Optimizations

MoveShortcuts touches slow Windows surfaces: shell COM objects, filesystem
metadata, PATH lookup, Start menu folders, UWP AppsFolder enumeration, and
external resolver commands. This document records the optimizations made so far,
what they improved, and the tradeoffs they introduced.

The measured local baseline during this round was a debug run from:

```text
MoveShortcuts\bin\Debug\net10.0-windows
```

The exact timings vary by run because Windows shell and filesystem calls are
noisy, but the broad improvement was from roughly 21.5 seconds to roughly 6.7
seconds for the current cached run.

## Summary

| Area | Change | Result | Tradeoff |
| --- | --- | --- | --- |
| Command shadow checks | Cache PATH/PATHEXT directory contents once per run. | Removed repeated `File.Exists` probing as the largest hotspot. | PATH snapshot can miss changes made mid-run, but the run is short-lived. |
| Shortcut copying | Skip copy when source/target metadata already matches. | Avoids unnecessary writes; small wall-clock effect on current config. | Depends on length, creation time, and last-write time matching. |
| COM shortcut creation | Reuse the `WScript.Shell` COM object for the process. | Small but real improvement. | Keeps a COM object alive until process exit. |
| UWP enumeration | Cache AppsFolder results with AppModel package signature validation. | Cached runs avoid AppsFolder enumeration; local cached run around 7.4s before later matcher work. | Cache must be invalidated by package signature and user/machine identity. |
| Option matching | Split literal option names from regex patterns. | Removed regex matching hotspot; local cached run improved to about 6.7s. | Literal keys containing regex metacharacters are treated as literal unless exact-match fails through a pattern-looking key. |

## Profiling Timeline

| Stage | Approximate local run time | Main hotspot |
| --- | ---: | --- |
| Initial profiled debug run | 21.5s | `File.Exists` inside command shadow checks |
| PATH conflict cache | 11-12s | UWP enumeration, COM shortcut creation, file copying |
| UWP cache warm run | 7.4s | COM shortcut creation, copy work, external target resolution, regex matching |
| Literal option matcher | 6.7s | COM shortcut creation, copy work, external target resolution |

## Details

### PATH Conflict Cache

The command-shadowing guard checks whether generated shortcuts would hide
existing commands on PATH. The first implementation recomputed PATH candidates
and called `File.Exists` repeatedly for every output name.

The optimized version indexes PATH directory contents once per environment
value and then checks command candidates in memory.

Important behavior preserved:

- PATH order still matters.
- PATHEXT order still matters.
- Shortcuts created earlier in the same run can still win before later external
  commands.

### Shortcut Copy Skip

`Helpers.Copy` now avoids overwriting a target when length, creation time, and
last-write time match the source.

This is conservative: if metadata differs, the file is copied. It is more about
avoiding needless churn than producing a dramatic timing win.

### COM Shell Reuse

Creating `.lnk` files goes through Windows Script Host COM. Reusing the shell
object reduced repeated COM setup overhead. Each individual shortcut COM object
is still released after saving.

This was kept because it improved timing without making the code much harder to
reason about.

### UWP AppsFolder Cache

AppsFolder is accurate, but reading all names and AUMIDs is expensive. The cache
stores the AppsFolder result and validates it with:

- schema version
- machine name
- user name
- user SID
- AppModel registry package signature

The cache file is:

```text
move-shortcuts-uwp-cache.json
```

The refresh flag is:

```text
MoveShortcuts --refresh-uwp-cache
```

Apps are stored as an array rather than a JSON object because AppsFolder can
contain display names that differ only by case. PowerShell's default
`ConvertFrom-Json` treats object keys case-insensitively and fails on such data.

### Literal Option Matching

The old matching path used regex fallback broadly:

```text
^config-key$
```

for many source items and UWP app names. Most config keys are literal names, so
that spent time in the regex engine unnecessarily.

The current `FileOptionMatcher` uses:

- case-insensitive exact lookup first
- regex fallback only for keys that look like regex patterns

This removed the regex hotspot in the latest profile.

## Experiments Rolled Back

### Idempotent Shortcut Save

I tried opening existing `.lnk` files, reading their target/arguments/icon/work
directory, and skipping `Save()` if unchanged.

Result: no wall-clock improvement. The COM property reads cost enough that the
skip did not pay for itself on this workload.

Decision: rolled back.

### External Target Cache

I tried caching results for dynamic targets such as:

```text
es ...
where ...
```

The profile no longer showed `RunCommandAndGetFirstLine` as a hotspot after the
cache was warm, but wall-clock time did not materially improve. That made the
extra cache file and stale-result behavior unjustified for now.

Decision: rolled back.

## Current Remaining Hotspots

The latest useful profile still points at:

- COM shortcut creation and save
- copying/link duplication work
- external resolver commands on cold runs
- general filesystem metadata checks

The biggest remaining gains probably require changing behavior, not just
tightening loops. Examples:

- only create UWP shortcuts selected by config instead of considering all cached
  UWP apps
- batch or defer some group copies
- store richer shortcut state to avoid COM reads/writes safely
- make dynamic target resolution explicitly cacheable per option

Those changes need careful design because they affect correctness and user
expectations.
