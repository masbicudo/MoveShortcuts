# UWP Cache Design

MoveShortcuts creates shortcuts for UWP/MSIX apps by enumerating the Windows
shell `AppsFolder` view. That view is accurate, but materializing every display
name and AppUserModelID is one of the remaining runtime costs.

This design introduces a conservative cache. It uses `AppsFolder` as the source
of truth and a cheap AppModel registry signature to decide whether the cached
result is still valid.

## Goals

- Avoid full UWP enumeration on normal runs when the installed package set has
  not changed.
- Keep `AppsFolder` as the correctness source.
- Make cache refresh explicit and easy to reason about.
- Leave room for future registry/manifest incremental repair, but do not depend
  on it for the first implementation.

## Non-Goals

- Do not replace `AppsFolder` with the undocumented AppModel registry data.
- Do not implement fuzzy or manifest-based repair in production yet.
- Do not silently trust a cache created for another user or machine.

## Cache File

Default location:

```text
move-shortcuts-uwp-cache.json
```

It lives beside `move-shortcuts-options.json`, so debug and published folders
can keep independent caches.

Proposed schema:

```json
{
  "schemaVersion": 1,
  "createdUtc": "2026-05-26T00:00:00Z",
  "updatedUtc": "2026-05-26T00:00:00Z",
  "machineName": "NOTE-AVELL-MASB",
  "userName": "masbi",
  "userSid": "S-1-5-21-...",
  "packageSignature": {
    "source": "HKCU AppModel Repository Packages",
    "packageCount": 591,
    "hash": "sha256:..."
  },
  "apps": [
    {
      "name": "Calculadora",
      "appUserModelId": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
    }
  ]
}
```

Apps are stored as an array instead of a JSON object because AppsFolder can
contain display names that differ only by case. Some JSON tooling, including
PowerShell's default `ConvertFrom-Json`, treats object keys case-insensitively.

## Package Signature

The signature is computed from:

```text
HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages
```

For the conservative first pass, the signature includes:

- sorted package key names
- package count
- SHA-256 hash of the sorted key-name list

The registry path is not treated as a public source of app truth. It is only a
cheap signal that package state probably changed.

## Runtime Behavior

Normal run:

1. If UWP sources are disabled, skip cache and enumeration.
2. If there are no UWP-name options, skip cache and enumeration.
3. Compute current package signature.
4. Try to read the cache.
5. Use the cache only if:
   - schema version matches
   - user SID matches
   - machine name matches
   - package signature matches
6. If the cache is missing, invalid, or stale:
   - enumerate `AppsFolder`
   - write a fresh cache atomically
   - use the fresh data

Forced refresh:

```text
MoveShortcuts --refresh-uwp-cache
```

This refreshes the cache before normal processing.

## Failure Behavior

- Broken cache JSON: ignore, refresh from `AppsFolder`.
- Registry signature unavailable: refresh from `AppsFolder`, write cache with
  the fallback signature.
- Cache write failure: continue using freshly enumerated apps and print a
  warning.
- AppsFolder enumeration failure: print a warning and skip UWP shortcut
  creation.

## Tests

Unit tests should cover:

- matching cache is used
- stale signature triggers refresh
- wrong user or machine invalidates cache
- broken cache triggers refresh
- cache write/read round trip
- no UWP options avoids cache/enumeration

Smoke tests should cover:

- `dotnet test --no-restore`
- `MoveShortcuts --help`
- `MoveShortcuts --quiet`
- a second `MoveShortcuts --quiet` run, which should use the cache

## Future Work

The research in `research/` found that registry + manifest + indirect-string
resolution can reconstruct package-style AUMIDs with high confidence on the
current machine. A future implementation can add incremental cache repair:

1. Diff package signatures to find changed package families.
2. Parse manifests for changed packages.
3. Resolve display names with `SHLoadIndirectString`.
4. Accept high-confidence candidates.
5. Fall back to `AppsFolder` for uncertain cases.
