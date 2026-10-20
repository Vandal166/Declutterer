# Smart Select 2.0 – evidence based clean-up suggestions

Smart Select no longer asks "is this big and old?". It asks four questions about every file and folder and answers each one with evidence:

| Output | Question | Range |
|---|---|---|
| **Junk likelihood** | How likely is this unnecessary? | 0–1 |
| **Deletion risk** | How likely is deleting it to break something or lose valuable data? | 0–1 |
| **Confidence** | How strong, diverse and consistent is the evidence? | 0–1 |
| **Reason codes** | Which signals produced the verdict? | list |

The result is shown in the tree (new *Assessment* column, tooltip lists the reasons) and in **Review & Cleanup** (one line per item). Smart Select only ticks checkboxes – nothing is deleted until you confirm in Review & Cleanup, which still defaults to the Recycle Bin.

## Tiers and modes

| Tier | Junk ≥ | Risk ≤ | Confidence ≥ | Meaning |
|---|---|---|---|---|
| **Safe** | 0.90 | 0.15 | 0.75 | near-certain junk |
| **Likely** | 0.75 | 0.35 | 0.55 | very probably junk |
| **Review** | 0.50 | 0.60 | – | possibly junk, look first |
| **Keep** | | | | evidence of use/value, or not enough evidence |
| **Protected** | | | | system location (never selectable) |

The **mode** selector next to the button picks the cut: *Conservative* = Safe, *Balanced* (default) = Safe + Likely, *Aggressive* = also Review items with confidence ≥ 0.40.

## "Old" is not "useless"

* Age is only **corroborating** evidence. Its strongest possible contribution (10+ years untouched) is lower than the prior gap, so **age alone can never produce a junk verdict** (see `AgeAlone_CanNeverMakeSomethingJunk`).
* "Last touched" is the newest of write time, creation time (a copied file keeps its old write time!), the newest activity anywhere inside a folder, and – only when NTFS actually maintains it – last-access time. Reliability of last-access is detected from the registry *and* from a sample of real timestamps.
* Real usage evidence: running programs, Run keys / services / Startup folder, install locations from the Uninstall registry keys, and the shell's **Recent items** (the `.lnk` targets are parsed, so "opened 3 weeks ago" is path-accurate).

## How the numbers are computed

Every rule emits `Signal`s (`ReasonCode`, evidence group, log-likelihood ratio, risk, human-readable detail, optional hard veto).

* **Junk** = `sigmoid(logit(prior 0.08) + Σ evidence)`. Evidence is grouped into independent families – *Location, Pattern, Content, Staleness, Ownership, Structure*. Within a family only the strongest disposable and strongest keep signal count, so correlated evidence (a folder named `Cache` inside a cache location) is never double counted.
* **Risk** = noisy-OR over the per-family maximum. Any hard veto forces risk ≥ 0.95 and junk ≤ 0.10.
* **Confidence** = `strength × agreement × diversity × coverage`. Evidence from one family is capped (0.6, or 0.78 if overwhelming); corroboration from other families lifts the cap smoothly, so **Safe always needs a second, independent opinion**. Conflicting evidence lowers *agreement*. *Coverage* drops when a folder could only be partly inspected (budget, unreadable entries) or was not inspected at all.

### Rules (all independent, all unit tested)

| Rule | Detects |
|---|---|
| `KnownLocationRule` | temp folders, browser/Electron/GPU/shader caches, crash dumps, WER, INetCache, npm/pip/NuGet/Gradle/Maven/Cargo caches. AppData patterns are matched *relative to the AppData root*, weak names (`cache`, `temp`) only for the item's own name |
| `RegenerableArtifactRule` | `node_modules`, `__pycache__`, `bin`/`obj`, `target`, `.vs`, `.gradle`, venvs … – generic names only count next to the project file that proves what they are |
| `FilePatternRule` | `.tmp/.dmp/.log/.bak`, stale installers (stronger if the app is installed), `name (1).ext` copies that are byte-identical to the original (size + start/middle/end chunks), archives already extracted next to themselves, empty files |
| `ContentRule` | share of documents/media/databases (risk ↑), keys/certificates/password stores (**veto**), only-logs/temp content, empty folders, executables (portable apps) |
| `OwnershipRule` | running process, startup/service reference, installed application (**vetoes**), recently opened, cloud-synced folder (deleting propagates), container folders (Temp, Downloads, AppData roots … – only their *contents* are candidates) |
| `OrphanedAppDataRule` | app data whose application is gone: no Uninstall entry / Program Files / Start Menu name matches the folder or vendor **and** nothing inside changed for ≥ 180 days. Silent when no installed-program inventory is available |
| `ProjectRule` | git repositories (**veto**), project folders |
| `SensitiveLocationRule` | `.ssh`, `.gnupg`, `.aws`, DPAPI/credential stores, UWP package data (**vetoes**), browser profiles, saved games, Windows-managed AppData |
| `StalenessRule` | bounded, smooth age curve; recency is *strong* evidence of life; neutralised inside caches (they are rewritten constantly) |

## Safety (defence in depth)

1. Smart Select **only ticks checkboxes**; deletion needs the existing Review & Cleanup confirmation (default: Recycle Bin).
2. **Hard vetoes** (`HardKeep`) can not be outvoted by any amount of junk evidence.
3. `PathSafetyValidator` is re-run on everything that could become selectable (fails closed); anything it blocks becomes **Protected**.
4. The selector never takes a folder if **any loaded descendant carries a veto**, never nests selections, never selects scan roots.
5. Un-inspected or partially inspected folders cannot reach *Safe* (coverage). A node that was not assessed is never selectable.
6. Reparse points / junctions are never followed.

## Speed

Two-stage cascade:

* **Stage A** – metadata only, every loaded node, parallel. Assessed top-down: below a folder that is already a strongly disposable region (cache, temp, `node_modules`) nothing is assessed, because the selector takes that folder whole.
* **Stage B** – bounded disk inspection (entry cap, depth cap, wall-clock budget, cancellation) **only** for folders stage A could not rule out. Folders proven alive (activity in the last 14 days) stop early. Completed results are memoised and shared between nested folders and between passes.
* **Deepening** – junk usually hides one or two levels below what is loaded (`node_modules` inside a project). Big, non-junk, non-vetoed folders are loaded one more level (≤ 3 rounds, ≤ 6 levels, ≤ 150 folders per round) through the existing `ITreeNavigationService`.

Measured on a synthetic profile with 167,800 files (this sandbox, 4 jobs): plain recursive size walk 415 ms; typical tree (133 nodes loaded) 207 ms; **fully expanded tree, 168k nodes loaded** 1.0 s (2.5×). Before top-down pruning the same case took 5.7 s.

## Tuning

All thresholds and the budget live in `SmartCleanupOptions`; the evidence strengths are the numbers in the rule tables (log-likelihood ratios: +4 ≈ odds ×55). They are hand-calibrated priors – if a verdict ever surprises you, the tooltip tells you exactly which signals produced it, which is the data needed to adjust a number.

## Known limits / roadmap

* Windows first. Linux/macOS use `DefaultSystemSnapshotProvider` (no installed-program inventory → no orphan detection).
* Scheduled tasks, Prefetch and jump lists are not consulted yet.
* App-data name matching is fuzzy; that is why orphans top out at *Likely* (*Review* for Roaming) unless the content itself is disposable.
* Deletion speed (Recycle Bin of huge folders) is unchanged.
* The old `SmartSelectionService` / `SmartSelectionScorer` are no longer used by the UI and can be removed together with their tests.
