# Smart Select 2.0 – evidence based clean-up suggestions

Smart Select no longer asks "is this big and old?". It asks four questions about every file and folder and answers each one with evidence:

| Output | Question | Range |
|---|---|---|
| **Junk score** | How strongly does the heuristic evidence point to junk? | 0–1 score |
| **Deletion-risk estimate** | How strongly do the signals indicate possible harm? | 0–1 estimate |
| **Evidence strength** | How strong, varied and consistent is the evidence? | 0–1 score |
| **Reason codes** | Which signals produced the verdict? | list |

The result is shown in the tree (new *Assessment* column, tooltip lists the reasons) and in **Review & Cleanup** (one line per item). Smart Select only ticks checkboxes – nothing is deleted until you confirm in Review & Cleanup, which still defaults to the Recycle Bin.

## Tiers and modes

| Tier | Junk score ≥ | Risk estimate ≤ | Evidence strength ≥ | Meaning |
|---|---|---|---|---|
| **Safe** | 0.90 | 0.15 | 0.75 | meets the most conservative heuristic thresholds |
| **Likely** | 0.75 | 0.35 | 0.55 | strong heuristic evidence; review before deletion |
| **Review** | 0.50 | 0.60 | – | possible junk; look first |
| **Keep** | | | | evidence of use/value, or not enough evidence |
| **Protected** | | | | system location (never selectable) |

The **mode** selector next to the button picks the cut: *Conservative* = Safe, *Balanced* (default) = Safe + Likely, *Aggressive* = also Review items with evidence strength ≥ 0.40. These categories use hand-calibrated heuristics and do not represent validated probabilities.

## "Old" is not "useless"

* Age is only **corroborating** evidence. Its strongest possible contribution (10+ years untouched) is lower than the prior gap, so **age alone can never produce a junk verdict** (see `AgeAlone_CanNeverMakeSomethingJunk`).
* "Last touched" is the newest of write time, creation time (a copied file keeps its old write time!), the newest activity anywhere inside a folder, and – only when NTFS actually maintains it – last-access time. Reliability of last-access is detected from the registry *and* from a sample of real timestamps.
* Real usage evidence: running programs, Run keys / services / Startup folder, install locations from the Uninstall registry keys, and the shell's **Recent items** (the `.lnk` targets are parsed, so "opened 3 weeks ago" is path-accurate).

## How the numbers are computed

Every rule emits `Signal`s (`ReasonCode`, configured evidence family, heuristic score contribution, risk estimate, human-readable detail, optional hard veto).

* **Junk score** = `sigmoid(logit(prior 0.08) + Σ heuristic evidence)`. Evidence is grouped into configured families – *Location, Pattern, Content, Staleness, Ownership, Structure*. Within a family only the strongest disposable and strongest keep signal count, so correlated evidence (a folder named `Cache` inside a cache location) is not double counted. The family labels are a modeling choice and do not establish statistical independence.
* **Deletion-risk estimate** = noisy-OR over the per-family maximum. Any hard veto forces the estimate to at least 0.95 and the junk score to at most 0.10.
* **Evidence strength** = `strength × agreement × diversity × coverage`. Safe explicitly requires positive support from at least two configured evidence families. Conflicting evidence lowers *agreement*. *Coverage* drops when a folder could only be partly inspected (budget, unreadable entries) or was not inspected at all.

### Rules (separate heuristics, all unit tested)

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
4. Before a directory can be selected whole, a bounded safety-only walk checks every descendant for scoped protections, credentials, sensitive paths, repository metadata, and reparse points. It never follows reparse points.
5. A directory is not auto-selected in any mode when that safety walk finds a blocker or cannot complete because of a budget, depth limit, unreadable entry, or other access failure. Its children are considered when available in the loaded tree.
6. The selector also checks loaded descendants for vetoes, never nests selections, and never selects scan roots. A node without a completed selection-safety check is never auto-selected.

## Speed

Two-stage cascade:

* **Stage A** – metadata only, every loaded node, parallel. A strongly disposable folder covers its loaded descendants only after its full descendant safety walk succeeds. If that walk finds a blocker or is incomplete, its loaded children are assessed.
* **Stage B** – bounded disk inspection (entry cap, depth cap, wall-clock budget, cancellation) for folders stage A could not rule out. Folders proven alive (activity in the last 14 days) may stop their detailed content walk early; a directory that could be selected whole still requires its separate complete safety walk. Completed results are memoised and shared between nested folders and between passes.
* **Deepening** – junk usually hides one or two levels below what is loaded (`node_modules` inside a project). Big, non-junk, non-vetoed folders are loaded one more level (≤ 6 rounds, ≤ 10 levels, ≤ 400 folders per round) through the existing `ITreeNavigationService`. Folders that look like projects / repositories are opened first (their build output is what we are after), then the rest by size - otherwise a few hundred big game or media folders use up the quota before any project is opened.
* **Budgets** – `TimeBudget` (30 s) is granted *separately* to the disk inspection and to the descendant safety scans of one pass, so walking big folders of old personal data can no longer starve the safety scans of the junk folders. A scan that merely timed out is never remembered by the session (a later pass retries it), and a pass that hits its budget no longer ends the run; `TotalTimeBudget` (3 min) bounds the whole run.
* **Lookups** – "is this path inside the install location of an installed program" is an ancestor lookup, not a scan over every uninstall entry. With ~300 entries (typical) the old scan made the safety walk ~10x slower and was the main reason large `node_modules` trees ran out of time.

Measured on a synthetic profile with 167,800 files (this sandbox, 4 jobs): plain recursive size walk 415 ms; typical tree (133 nodes loaded) 207 ms; **fully expanded tree, 168k nodes loaded** 1.0 s (2.5×). Before top-down pruning the same case took 5.7 s.

## Why Smart Select may find little

The summary line under the selection explains what limited the result, so "found little" can be told apart from "could not look":

| Message | Meaning / what to do |
|---|---|
| *Hidden folders (AppData, ProgramData, .git, .vs) were not scanned* | The scan skipped everything with the Hidden attribute. Caches, temp files and leftovers of removed apps live there. Enable **Include hidden folders and files** in Scan Settings (off by default: it also exposes those folders to *Select All*). |
| *N large folders were not opened (depth / round limit)* | More folders could have been looked into than the limits allow. Run Smart Select again (opened folders stay loaded) or scan a narrower root. |
| *N folders were not inspected / Time budget reached* | The disk inspection ran out of time for these folders; they cannot reach Safe. |
| *N folders could not be fully safety-checked* | Too many entries, depth limit, unreadable entries or time. They are never auto-selected; you can still tick them by hand. |
| *Stopped at the overall time limit* | `TotalTimeBudget` was reached. Run again to continue deeper. |

Age on its own is deliberately never enough for a "junk" verdict (see above), so very old folders of unrecognised personal files stay *Keep* in every mode.

## Tuning

All thresholds and the budget live in `SmartCleanupOptions`; rule strengths are hand-tuned heuristic log-odds contributions. They are not empirically validated likelihood ratios, and the displayed 0–100 values are scores rather than probability percentages. The tooltip lists the signals behind each verdict. Numeric weights stay unchanged until a manually reviewed evaluation set supports a calibration change.

## Evaluation gate

Before describing Smart Select scores as calibrated probabilities or changing weights, evaluate a manually reviewed, held-out candidate set. Report Safe precision and false-safe rate overall and by category, calibration metrics where the reviewed labels support them, and the share of candidates left unresolved. A confirmed valuable or protected candidate in Safe blocks Safe automation for the affected category until its rule is corrected.

## Known limits / roadmap

* Windows first. Linux/macOS use `DefaultSystemSnapshotProvider` (no installed-program inventory → no orphan detection).
* Hidden entries are only scanned when *Include hidden* is enabled; without it the AppData based rules (caches, temp, orphaned app data, crash dumps) cannot fire on a whole-drive scan.
* Duplicate detection only covers `name (1).ext` / `Copy of` siblings that are byte-identical; there is no cross-folder content hashing yet.
* Scheduled tasks, Prefetch and jump lists are not consulted yet.
* App-data name matching is fuzzy; that is why orphans top out at *Likely* (*Review* for Roaming) unless the content itself is disposable.
* Deletion speed (Recycle Bin of huge folders) is unchanged.
* Deletion authorization remains path-based. Object identity and descendant contents are not revalidated immediately before recursive deletion; track that as a separate hardening phase.
* The old `SmartSelectionService` / `SmartSelectionScorer` are no longer used by the UI and can be removed together with their tests.
