# UWP Enumeration Research

This folder is a small research lab for reducing the cost of UWP app
enumeration in MoveShortcuts.

The production code currently treats the shell `AppsFolder` view as the source
of truth because it exposes the launchable app names and AppUserModelIDs that
Windows actually shows to the user. The problem is that reading every app name
and AUMID through the shell is slow enough to matter during normal CLI runs.

The research question is:

> Can the fast AppModel registry repository, package manifests, and resource
> resolution reproduce enough of `AppsFolder` to cache or incrementally repair
> UWP shortcut data?

Generated datasets are written to `research/output/` and ignored by git because
they contain local installed-app names, AUMIDs, package names, and package paths.

## Map Of The Investigation

| Step | Hypothesis | Method | Result |
| --- | --- | --- | --- |
| 1 | `AppsFolder` is accurate but expensive. | Timed shell AppsFolder enumeration and materializing names/AUMIDs. | Count-only was fast, but reading names/paths took about 2.7s locally. |
| 2 | AppModel registry package keys can cheaply detect package state changes. | Enumerated `HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages`. | Registry package-key enumeration took about 0.1s locally. |
| 3 | Registry package/subkey names can reconstruct AUMIDs. | Derived `PackageFamilyName!AppId` candidates from package keys and app subkeys. | Naive subkey matching covered only 47 of 139 package-style AppsFolder AUMIDs. |
| 4 | Default app-id guesses can improve coverage. | Added default candidates like `PackageFamily!App` and `PackageFamily!HostedApp` when no subkeys exist. | Coverage rose to 130 of 139 package-style AppsFolder AUMIDs. |
| 5 | Feature extraction can separate real launchable apps from internal packages. | Decomposed package names, publisher IDs, root folder kinds, app-id shapes, display-name shapes. | Several extracted features showed meaningful information gain. |
| 6 | Manifest parsing can recover missing application IDs. | Parsed `PackageRootFolder\AppxManifest.xml` and extracted `<Application Id="...">`. | Package-style AppsFolder entries not predicted dropped from 9 to 0. |
| 7 | Display names can be resolved without full AppsFolder enumeration. | Resolved `ms-resource:` values through `SHLoadIndirectString`. | Exact name matches among matched AUMID rows rose from 200/327 to 277/327. |
| 8 | Fuzzy/token matching can give confidence scores. | Computed normalized token containment, token Jaccard, and Levenshtein-ratio bins. | Strong name similarity reached 100% precision and 95% recall for known package families on this dataset. |

## Current Evidence Snapshot

From the latest local dataset:

| Metric | Value |
| --- | ---: |
| Shell `AppsFolder` entries | 574 |
| Registry/manifest candidate rows | 2441 |
| Unique package-style AppsFolder AUMIDs represented by candidates | 139 |
| Package-style AppsFolder entries not predicted | 0 |
| Name exact matches among matched candidate rows | 277 / 327 |

Most informative features in the latest run:

| Feature | Information gain |
| --- | ---: |
| `LevenshteinRatioBin` | 0.5249 |
| `TokenJaccardBin` | 0.4321 |
| `TokenContainmentBin` | 0.4321 |
| `CandidateAppId` | 0.3546 |
| `PublisherId` | 0.1838 |
| `PackageRootKind` | 0.1557 |
| `PackageFamilyAppearsInAppsFolder` | 0.1460 |

The most promising practical classifier so far:

| Rule | Precision | Recall |
| --- | ---: | ---: |
| Known package families + strong name similarity | 100.0% | 95.0% |
| Known package families + manifest candidates | 70.8% | 97.8% |
| Known package families + `App` candidates | 94.9% | 80.6% |

## Working Model

The current research supports a conservative architecture:

1. Use `AppsFolder` once to seed or verify a trusted cache.
2. Use the AppModel registry package set as a cheap package-state signature.
3. When package state changes, inspect changed package families.
4. Use package manifests to extract candidate app IDs.
5. Use `SHLoadIndirectString` to resolve localized display names.
6. Use token/fuzzy similarity as a confidence signal.
7. Fall back to `AppsFolder` when confidence is low.

This avoids betting the production app on undocumented registry behavior while
still creating a path toward much faster normal runs.

## Workflow

Collect the dataset:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File research/collect-uwp-appmodel-dataset.ps1
```

Analyze feature information gain:

```powershell
python research/analyze-uwp-appmodel-dataset.py
```

The collector writes:

- `appsfolder.csv`: shell `AppsFolder` entries.
- `registry-candidates.csv`: candidate AUMIDs derived from registry data, default rules, and manifests.
- `candidate-dataset.csv`: registry/manifest candidates labeled against `AppsFolder`.
- `appsfolder-unmatched.csv`: package-style `AppsFolder` entries not predicted by candidates.

The analyzer writes:

- `analysis-summary.md`: information gain tables and match diagnostics.

## Related Notes

- [Methodology](methodology.md) describes data sources, features, labels, and metrics.
- [Findings](findings.md) records the main observations and design implications.
