# Methodology

This document describes the current research protocol for evaluating whether
MoveShortcuts can avoid full shell `AppsFolder` enumeration during normal runs.

## Sources

### Shell AppsFolder

`AppsFolder` is collected with:

```powershell
(New-Object -ComObject Shell.Application).
  NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}").
  Items()
```

For each item the collector records:

- display name
- shell path / AUMID
- whether the path is package-style, meaning it contains `!`
- package family name and app ID parsed from package-style AUMIDs

This source is treated as the label source because it represents what Windows
exposes as launchable shell apps.

### AppModel Registry Repository

The registry source is:

```text
HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages
```

For each package key, the collector extracts:

- package full name
- package family name
- package name, version, architecture, resource ID, publisher ID
- package root folder
- package display name
- app subkeys and capability values

This source is fast and user-scoped, but it is treated as an implementation
detail rather than a supported public contract.

### AppxManifest.xml

When `PackageRootFolder\AppxManifest.xml` exists and is readable, the collector
extracts each:

```xml
<Application Id="...">
```

and its `uap:VisualElements` display name. Manifest application IDs are used to
construct additional `PackageFamilyName!ApplicationId` candidates.

### Indirect String Resolution

Display names may be stored as `ms-resource:` references. The collector calls
`SHLoadIndirectString` from `shlwapi.dll` to resolve forms such as:

```text
@{PackageFullName?ms-resource://...}
@{C:\Path\To\resources.pri?ms-resource://...}
```

Resolved names are used before fuzzy/token matching.

## Candidate Generation

The collector currently creates candidates from three paths:

| Candidate source | Rule |
| --- | --- |
| `registry-subkey` | `PackageFamilyName!AppSubkeyName` |
| `registry-default` | `PackageFamilyName!App` and `PackageFamilyName!HostedApp` when no app subkeys exist |
| `manifest` | `PackageFamilyName!ApplicationId` from `AppxManifest.xml` |

Candidates are labeled by exact AUMID match against package-style `AppsFolder`
entries.

## Feature Extraction

The research intentionally extracts simple, explainable features before trying
more complex models.

Package features:

- package family appears in AppsFolder
- package name token count and shape
- publisher ID
- architecture
- resource ID
- package root kind, such as `WindowsApps`, `SystemApps`, `LocalPackages`

Candidate features:

- candidate source
- candidate rule
- candidate app ID
- app ID length bin
- app ID token count
- whether app ID contains dot, dash, or digit

Display-name features:

- resolved package display name
- resolved capability application name
- resolved manifest display name
- token count
- length bin
- whether value still looks like a resource reference

Similarity features:

- normalized exact match
- token Jaccard similarity
- token containment
- Levenshtein ratio
- binned versions of each score

Normalization removes accents, lowercases, removes punctuation, and collapses
whitespace before token and distance calculations.

## Metrics

The analyzer reports:

- exact candidate-row AUMID matches
- unique-AUMID precision and recall
- package-style AppsFolder AUMIDs not predicted
- information gain for candidate features
- match rates by candidate rules and feature values

Information gain is computed as:

```text
IG(label, feature) = H(label) - H(label | feature)
```

where the label is `AumidExactMatch`.

## Interpretation Rules

Row-level precision can be misleading because multiple candidate-generation
rules can produce the same AUMID. For design decisions, prefer the unique-AUMID
classifier table.

`AppsFolder` remains the trusted source. Registry/manifest reconstruction is
currently considered a cache-repair strategy, not a replacement for all shell
behavior.
