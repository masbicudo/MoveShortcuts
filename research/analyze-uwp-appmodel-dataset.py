#!/usr/bin/env python
"""Analyze AppModel registry candidates against AppsFolder labels.

The script intentionally uses only the Python standard library so the research
workflow does not add a dependency stack to the application.
"""

from __future__ import annotations

import csv
import math
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT / "output"
DATASET = OUTPUT / "candidate-dataset.csv"
UNMATCHED = OUTPUT / "appsfolder-unmatched.csv"
SUMMARY = OUTPUT / "analysis-summary.md"


def as_bool(value: str) -> bool:
    """Parse PowerShell CSV booleans."""
    return str(value).strip().lower() == "true"


def entropy(labels: list[bool]) -> float:
    """Compute binary entropy in bits for the target label."""
    total = len(labels)
    if total == 0:
        return 0.0

    counts = Counter(labels)
    result = 0.0
    for count in counts.values():
        p = count / total
        result -= p * math.log2(p)
    return result


def information_gain(rows: list[dict[str, str]], feature: str, label: str) -> float:
    """Measure how much knowing a feature reduces uncertainty about a label."""
    labels = [as_bool(row[label]) for row in rows]
    base = entropy(labels)
    groups: dict[str, list[bool]] = defaultdict(list)
    for row in rows:
        groups[str(row.get(feature, ""))].append(as_bool(row[label]))

    remainder = 0.0
    total = len(rows)
    for group_labels in groups.values():
        remainder += (len(group_labels) / total) * entropy(group_labels)
    return base - remainder


def value_counts(rows: list[dict[str, str]], columns: list[str], label: str) -> list[tuple[tuple[str, ...], int, int, float]]:
    """Count feature values and their positive-label rates."""
    counts: Counter[tuple[str, ...]] = Counter()
    positives: Counter[tuple[str, ...]] = Counter()
    for row in rows:
        key = tuple(row.get(column, "") for column in columns)
        counts[key] += 1
        if as_bool(row[label]):
            positives[key] += 1

    result = []
    for key, count in counts.items():
        positive = positives[key]
        result.append((key, count, positive, positive / count if count else 0.0))
    result.sort(key=lambda item: (item[3], item[2], item[1]), reverse=True)
    return result


def classifier_metrics(
    rows: list[dict[str, str]],
    unmatched_count: int,
    name: str,
    predicate,
) -> list[object]:
    """Evaluate row-level precision/recall for a candidate-selection rule."""
    predicted = [row for row in rows if predicate(row)]
    true_positive = sum(1 for row in predicted if as_bool(row["AumidExactMatch"]))
    false_positive = len(predicted) - true_positive
    total_actual = sum(1 for row in rows if as_bool(row["AumidExactMatch"])) + unmatched_count
    false_negative = total_actual - true_positive
    precision = true_positive / len(predicted) if predicted else 0.0
    recall = true_positive / total_actual if total_actual else 0.0
    return [
        name,
        len(predicted),
        true_positive,
        false_positive,
        false_negative,
        f"{precision:.1%}",
        f"{recall:.1%}",
    ]


def unique_aumid_metrics(
    rows: list[dict[str, str]],
    unmatched_count: int,
    name: str,
    predicate,
) -> list[object]:
    """Evaluate precision/recall after collapsing duplicate candidate AUMIDs."""
    predicted_aumids = {row["CandidateAumid"] for row in rows if predicate(row)}
    true_aumids = {row["CandidateAumid"] for row in rows if as_bool(row["AumidExactMatch"])}
    true_positive = len(predicted_aumids & true_aumids)
    false_positive = len(predicted_aumids - true_aumids)
    total_actual = len(true_aumids) + unmatched_count
    false_negative = total_actual - true_positive
    precision = true_positive / len(predicted_aumids) if predicted_aumids else 0.0
    recall = true_positive / total_actual if total_actual else 0.0
    return [
        name,
        len(predicted_aumids),
        true_positive,
        false_positive,
        false_negative,
        f"{precision:.1%}",
        f"{recall:.1%}",
    ]


def markdown_table(headers: list[str], rows: list[list[object]]) -> str:
    """Render compact GitHub-flavored Markdown tables for the lab notebook."""
    lines = [
        "| " + " | ".join(headers) + " |",
        "| " + " | ".join("---" for _ in headers) + " |",
    ]
    for row in rows:
        lines.append("| " + " | ".join(str(value) for value in row) + " |")
    return "\n".join(lines)


def main() -> None:
    """Load the generated CSVs and write the Markdown analysis summary."""
    if not DATASET.exists():
        raise SystemExit(f"Dataset not found: {DATASET}")

    with DATASET.open("r", encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))

    unmatched_count = 0
    if UNMATCHED.exists():
        with UNMATCHED.open("r", encoding="utf-8-sig", newline="") as f:
            unmatched_count = sum(1 for _ in csv.DictReader(f))

    label = "AumidExactMatch"
    positives = sum(1 for row in rows if as_bool(row[label]))
    unique_matching_aumids = {row["CandidateAumid"] for row in rows if as_bool(row[label])}
    base_entropy = entropy([as_bool(row[label]) for row in rows])

    features = [
        "CandidateSource",
        "CandidateRule",
        "CandidateAppId",
        "CandidateAppIdLengthBin",
        "CandidateAppIdTokenCount",
        "CandidateAppIdHasDot",
        "CandidateAppIdHasDash",
        "CandidateAppIdHasDigit",
        "ChildCount",
        "HasCapabilities",
        "HasUrlAssociations",
        "IsDefaultCandidate",
        "HasManifestApplication",
        "PackageFamilyAppearsInAppsFolder",
        "PackageRootKind",
        "PackageArchitecture",
        "PackageResourceId",
        "PublisherId",
        "PackageNameLengthBin",
        "PackageNameTokenCount",
        "PackageNameHasDot",
        "PackageNameHasDash",
        "PackageDisplayNameLengthBin",
        "PackageDisplayNameTokenCount",
        "PackageDisplayNameHasDot",
        "PackageDisplayNameHasDash",
        "RegistryBestNameIsResource",
        "PackageDisplayNameIsResource",
        "TokenJaccardBin",
        "TokenContainmentBin",
        "LevenshteinRatioBin",
    ]
    gains = sorted(
        ((feature, information_gain(rows, feature, label)) for feature in features),
        key=lambda item: item[1],
        reverse=True,
    )

    rule_rows = [
        [key[0], count, positive, f"{rate:.1%}"]
        for key, count, positive, rate in value_counts(rows, ["CandidateRule"], label)
    ]
    high_signal_features = [
        "PublisherId",
        "PackageRootKind",
        "PackageDisplayNameTokenCount",
        "PackageDisplayNameHasDot",
        "CandidateAppIdLengthBin",
        "CandidateSource",
        "TokenContainmentBin",
        "LevenshteinRatioBin",
    ]
    high_signal_rows = []
    for feature in high_signal_features:
        for key, count, positive, rate in value_counts(rows, [feature], label)[:8]:
            high_signal_rows.append([feature, key[0], count, positive, f"{rate:.1%}"])

    combo_rows = [
        [key[0], key[1], key[2], count, positive, f"{rate:.1%}"]
        for key, count, positive, rate in value_counts(
            rows,
            ["CandidateRule", "CandidateAppId", "PackageFamilyAppearsInAppsFolder"],
            label,
        )[:20]
    ]
    gain_rows = [[feature, f"{gain:.4f}"] for feature, gain in gains]
    classifier_rows = [
        classifier_metrics(
            rows,
            unmatched_count,
            "all registry candidates",
            lambda row: True,
        ),
        classifier_metrics(
            rows,
            unmatched_count,
            "known AppsFolder package families",
            lambda row: as_bool(row["PackageFamilyAppearsInAppsFolder"]),
        ),
        classifier_metrics(
            rows,
            unmatched_count,
            "known families, no HostedApp default",
            lambda row: as_bool(row["PackageFamilyAppearsInAppsFolder"])
            and row["CandidateRule"] != "no-subkey-HostedApp",
        ),
        classifier_metrics(
            rows,
            unmatched_count,
            "known families, App candidates",
            lambda row: as_bool(row["PackageFamilyAppearsInAppsFolder"])
            and row["CandidateAppId"] == "App",
        ),
        classifier_metrics(
            rows,
            unmatched_count,
            "known families, subkey candidates",
            lambda row: as_bool(row["PackageFamilyAppearsInAppsFolder"])
            and row["CandidateRule"] == "subkey",
        ),
    ]
    unique_classifier_rows = [
        unique_aumid_metrics(
            rows,
            unmatched_count,
            "all registry candidates",
            lambda row: True,
        ),
        unique_aumid_metrics(
            rows,
            unmatched_count,
            "manifest candidates",
            lambda row: row["CandidateSource"] == "manifest",
        ),
        unique_aumid_metrics(
            rows,
            unmatched_count,
            "known families, manifest",
            lambda row: as_bool(row["PackageFamilyAppearsInAppsFolder"])
            and row["CandidateSource"] == "manifest",
        ),
        unique_aumid_metrics(
            rows,
            unmatched_count,
            "known families, App candidates",
            lambda row: as_bool(row["PackageFamilyAppearsInAppsFolder"])
            and row["CandidateAppId"] == "App",
        ),
        unique_aumid_metrics(
            rows,
            unmatched_count,
            "known families, strong name similarity",
            lambda row: as_bool(row["PackageFamilyAppearsInAppsFolder"])
            and row["TokenContainmentBin"] in ("1.00", "0.75-0.99"),
        ),
    ]

    matched_rows = [row for row in rows if as_bool(row[label])]
    name_exact = sum(1 for row in matched_rows if as_bool(row["NameExactMatch"]))
    name_normalized = sum(1 for row in matched_rows if as_bool(row["NameNormalizedMatch"]))

    summary = []
    summary.append("# UWP AppModel Research Summary")
    summary.append("")
    summary.append("## Dataset")
    summary.append("")
    summary.append(f"- Registry candidate rows: {len(rows)}")
    summary.append(f"- AUMID exact matches: {positives}")
    summary.append(f"- Unique matched AUMIDs represented by candidates: {len(unique_matching_aumids)}")
    summary.append(f"- AUMID candidate precision: {positives / len(rows):.1%}" if rows else "- AUMID candidate precision: n/a")
    summary.append(f"- AppsFolder package entries not predicted: {unmatched_count}")
    summary.append(f"- Base label entropy: {base_entropy:.4f} bits")
    summary.append(f"- Name exact matches among matched AUMIDs: {name_exact} / {len(matched_rows)}")
    summary.append(f"- Name normalized matches among matched AUMIDs: {name_normalized} / {len(matched_rows)}")
    summary.append("")
    summary.append("## Information Gain")
    summary.append("")
    summary.append(markdown_table(["Feature", "Information gain"], gain_rows))
    summary.append("")
    summary.append("## Match Rate By Candidate Rule")
    summary.append("")
    summary.append(markdown_table(["CandidateRule", "Rows", "Matches", "Match rate"], rule_rows))
    summary.append("")
    summary.append("## High Signal Feature Values")
    summary.append("")
    summary.append(markdown_table(["Feature", "Value", "Rows", "Matches", "Match rate"], high_signal_rows))
    summary.append("")
    summary.append("## Candidate Rules As Classifiers")
    summary.append("")
    summary.append(markdown_table(
        ["Rule", "Predicted", "TP", "FP", "FN", "Precision", "Recall"],
        classifier_rows,
    ))
    summary.append("")
    summary.append("## Candidate Rules As Unique-AUMID Classifiers")
    summary.append("")
    summary.append(markdown_table(
        ["Rule", "Predicted unique AUMIDs", "TP", "FP", "FN", "Precision", "Recall"],
        unique_classifier_rows,
    ))
    summary.append("")
    summary.append("## Top Candidate Feature Combos")
    summary.append("")
    summary.append(markdown_table(
        ["CandidateRule", "CandidateAppId", "PackageFamilyAppearsInAppsFolder", "Rows", "Matches", "Match rate"],
        combo_rows,
    ))
    summary.append("")

    SUMMARY.write_text("\n".join(summary), encoding="utf-8")
    print(f"Wrote {SUMMARY}")
    print("\n".join(summary[:18]))


if __name__ == "__main__":
    main()
