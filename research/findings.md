# Findings

This document records the main findings from the UWP enumeration research.

## Summary

The AppModel registry repository plus package manifests can reconstruct all
package-style AppsFolder AUMIDs observed in the latest local dataset. Display
name resolution through `SHLoadIndirectString` greatly improves confidence
scoring, making token/fuzzy matching the strongest feature family.

The best current design is not to replace `AppsFolder` entirely. The evidence
supports using AppsFolder as a seed/verification source and using registry,
manifest, and resource data to cheaply repair or validate the cache.

## Progress Table

| Experiment | Coverage / result | Interpretation |
| --- | --- | --- |
| Registry subkeys only | 47 / 139 package-style AUMIDs matched | Too incomplete. |
| Registry subkeys plus default `App` / `HostedApp` | 130 / 139 matched | Useful, but still misses real app IDs like `Netflix.App`. |
| Feature extraction | `PublisherId`, `PackageRootKind`, app-id shape became informative | String/package decomposition matters. |
| Manifest parsing | 0 package-style AppsFolder AUMIDs left unmatched | Manifest IDs are the missing recall source. |
| `SHLoadIndirectString` name resolution | Name exact matches rose from 200 / 327 to 277 / 327 candidate rows | Resolving `ms-resource:` is essential for display-name matching. |
| Strong name similarity on known families | 100.0% precision, 95.0% recall | Good candidate for high-confidence incremental repair. |

## Latest Dataset Snapshot

| Metric | Value |
| --- | ---: |
| AppsFolder entries | 574 |
| Registry/manifest candidate rows | 2441 |
| AUMID exact-match candidate rows | 327 |
| Unique matched AUMIDs represented by candidates | 139 |
| Package-style AppsFolder entries not predicted | 0 |
| Name exact matches among matched rows | 277 / 327 |
| Name normalized matches among matched rows | 277 / 327 |

## Information Gain

Top features from the latest analysis:

| Feature | Information gain |
| --- | ---: |
| `LevenshteinRatioBin` | 0.5249 |
| `TokenJaccardBin` | 0.4321 |
| `TokenContainmentBin` | 0.4321 |
| `CandidateAppId` | 0.3546 |
| `PublisherId` | 0.1838 |
| `PackageRootKind` | 0.1557 |
| `PackageFamilyAppearsInAppsFolder` | 0.1460 |
| `PackageDisplayNameTokenCount` | 0.1447 |
| `PackageDisplayNameLengthBin` | 0.1286 |
| `CandidateAppIdLengthBin` | 0.1242 |

The shift after display-name resolution is important. Before resolving
`ms-resource:` values, package/display-name shape was useful mainly as a weak
filter. After resolution, direct similarity features became the strongest
signals.

## Candidate Classifiers

Unique-AUMID classifier results:

| Rule | Predicted unique AUMIDs | Precision | Recall |
| --- | ---: | ---: | ---: |
| All registry/manifest candidates | 621 | 22.4% | 100.0% |
| Manifest candidates | 318 | 42.8% | 97.8% |
| Known package families + manifest candidates | 192 | 70.8% | 97.8% |
| Known package families + `App` candidates | 118 | 94.9% | 80.6% |
| Known package families + strong name similarity | 132 | 100.0% | 95.0% |

This suggests a two-tier strategy:

1. Automatically accept high-confidence candidates with strong name similarity.
2. Fall back to AppsFolder refresh for the remaining low-confidence or ambiguous
   cases.

## Practical Implications

### Cache Invalidation

The registry package set is a good cheap signature source. Hashing sorted
package key names should detect most package installs, removals, and updates
without paying the AppsFolder enumeration cost.

### Cache Repair

For changed package families, the app could:

1. Read package registry values.
2. Parse package manifests.
3. Resolve display names through `SHLoadIndirectString`.
4. Generate candidate AUMIDs.
5. Accept candidates with strong name similarity.
6. Use AppsFolder only if the package remains ambiguous.

### Production Caution

The AppModel registry path is not treated as a stable public API. If this
research becomes production code, AppsFolder should remain the correctness
fallback.

## Open Questions

- Does the same precision/recall hold on machines with fewer PWAs and Store
  apps?
- How often do package manifests become unreadable because of permissions?
- Can changed package families be detected reliably enough from registry key
  diffs alone?
- What is the best on-disk cache schema for recording source signatures and
  confidence scores?
- Can confidence rules be kept simple enough to maintain without a trained
  model?
