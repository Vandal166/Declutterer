# Smart Select evaluation

The scoring weights remain heuristic until they are evaluated against manually reviewed candidates. Use a held-out set for the report; keep candidate identifiers anonymous and do not put paths or personal data in the CSV.

## Corpus format

Start with [`SmartSelectEvaluationTemplate.csv`](SmartSelectEvaluationTemplate.csv). Each row represents one candidate. Record the expected junk, safe-to-remove, and harmful-deletion labels from manual review, then record the predicted tier, scores, coverage, and safety-scan result from the same Smart Select run. Use `train` for data used to tune a future model and `holdout` for candidates reserved for evaluation. The report script reads only `holdout` rows and flags a category when a Safe prediction has a negative safe-to-remove label or a positive harm label.

`ExpectedJunk`, `ExpectedSafe`, and `ExpectedHarm` accept `true`, `false`, or blank when that label cannot be established. `JunkScore`, `DeletionRisk`, `Confidence`, and `SafetyCoverage` are values from 0 through 1. `SelectionSafetyComplete` is `true` or `false`.

## Report

Run `tools/Measure-SmartSelectEvaluation.ps1 -InputCsv <path-to-reviewed-csv>`. The script reports overall and per-category Safe precision, false-safe rate, diagnostic Brier scores where labels exist, mean inspection coverage, complete safety-scan share, unresolved share, and Safe-category gate status. A category with a reviewed unsafe or harmful Safe prediction is marked `BLOCKED`; categories without enough labels are marked `UNASSESSED`. Pass `-OutputPath <path>` to save the Markdown report.

The Brier scores are diagnostics for comparison with reviewed labels; they do not by themselves establish that the displayed values are probabilities. Before describing scores as calibrated or changing weights, review the held-out report. Any manually confirmed valuable or protected candidate in Safe blocks Safe automation for its category until the responsible rule is corrected and reevaluated.
