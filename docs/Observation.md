# Observation – watching suspicious folders over time

Smart Select answers "does the evidence say this is junk?" from a single moment. Some folders stay at **Review** or **Likely** because nothing at that moment proves they are unused; other folders are **Keep** despite independent evidence that they are stale. Observation adds evidence by watching eligible folders over time and reporting activity back to Smart Select.

Observation never deletes, never selects and never overrides a safety check. It can only (a) demote an item that was seen in use, or (b) let a _completed, still-valid quiet period_ lift an item that is already suspicious by one step.

It is **off by default**. The first time it is switched on, the Watchlist page shows exactly what is stored and for how long and asks for agreement.

## What is stored

Only on this computer, in `ledger.db` (SQLite; `%LOCALAPPDATA%\Declutterer\observation\` on Windows):

| Table                                  | Contents                                                                                                                      |
| -------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `watch_item`                           | the folder's path, size when added, class, state, counters, the dates that matter, and two _fingerprints_ (see below)         |
| `checkin`                              | per check: when, the verdict, which sources spoke, covered days added, and the number of entries created / modified / deleted |
| `covered_day`                          | the UTC dates on which this computer was proven to be in use                                                                  |
| `own_activity`                         | intervals in which Declutterer itself was reading the disk (scans, Smart Select, check-ins)                                   |
| `run_log`, `volume_cursor`, `settings` | the last runs, change-journal cursors (reserved), the on/off switch, the consent and the cross-process run lock               |

A **fingerprint** is a hash over the names, kinds, sizes and last-write times of everything in the folder, plus a short hash per first-level entry (keyed by a hash of the entry's name: **no file or folder names are stored**). It contains **no file content** and **no per-file access log**: nothing records which files you open.

Retention: finished entries are forgotten after 90 days (pinned ones are kept: they are the "never suggest this again" memory), check records after 180 days, heartbeat days and own-activity after 400. **Delete all observation data** on the Watchlist page empties every table in one click (the consent stays).

## The lifecycle of a watched folder

Suggestions are discovered by a separate walk of the selected scan roots after Smart Select computes its selection plan. This makes discovery independent of which branches the lazy results tree happened to load. It uses the same hidden-folder setting and directory filters, one system snapshot and one reference time for the run. A folder must have complete facts and a complete safety scan; inaccessible, unsafe, or otherwise incomplete folders are never suggested. The walk reports skipped hidden, filtered, system, reparse-point and inaccessible folders, plus depth and per-folder entry limits. Cancelled or limited discovery is marked partial.

An eligible **unselected** folder can be suggested when Smart Select assesses it as **Review** or **Likely**, or when it is **Keep** with existing `LongUntouched` or `OrphanedAppData` evidence and passes a separate complete safety scan. Suggestions exclude selected ancestors and descendants, existing watches, sensitive locations, credentials, valuable content, cloud-synced folders, and safety blockers. The old 16 MB threshold is a ranking hint only; smaller folders remain eligible. Suggestions are stably ordered, nested candidates collapse to their outermost folder, and at most 200 active folders can be watched. The summary reports available capacity and how many candidates exceeded it. Watching does not select or delete a folder.

How long a folder must stay quiet depends on its class:

| Class                            | Required covered days |
| -------------------------------- | --------------------- |
| App data (under an AppData root) | 28                    |
| Generic folder                   | 45                    |
| Project folder                   | 60                    |

| State                          | Meaning                                                                 | Leaves when                                                                                                                          |
| ------------------------------ | ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| **Watching**                   | being observed; the quiet clock runs                                    | strong use → Alive · enough covered days → QuietEligible · too many inconclusive check-ins → Unobservable · gone → Resolved(Missing) |
| **Alive**                      | strong use was seen; shown as _observed in use_ for 30 days             | the cooldown ends → Watching with a fresh baseline (after 3 such resets → Retired)                                                   |
| **QuietEligible**              | the window was completed without strong use; valid for 30 days          | strong use → Alive · validity ends → Watching with a fresh baseline                                                                  |
| **Unobservable** / **Retired** | given up on; can be re-enrolled from the Watchlist                      | –                                                                                                                                    |
| **Resolved**                   | Deleted, Missing, Declined (stopped watching) or Pinned (never suggest) | purged after 90 days (pinned: kept)                                                                                                  |

Pinned and snoozed are _flags_, not states. A pinned or snoozed item is a veto in Smart Select ("kept by you"); a snoozed item is also skipped by check-ins.

The transitions are one pure function, `ObservationStateMachine.Transition(entry, result, now, options)`; the table above is a unit test.

### Covered days, not calendar days

A **covered day** is a UTC date on which the computer was proven to be in use: a check-in ran (when the app starts, when you press _Check now_, and – if you opted in – from the daily background task), or a change journal showed activity. An item's covered days are the heartbeat days strictly after its quiet-since day, and only a _conclusive_ comparison turns them into progress. A laptop that slept for a month contributes nothing for that month: **30 calendar days with 3 check-ins are 3 covered days.** If the calendar time exceeds three times the required days and there still are not enough covered days, every further check-in is _inconclusive_ (a machine that is rarely on can not be observed), and after 6 inconclusive check-ins in a row the item becomes Unobservable.

### What counts as use

| Source              | Strength                                                                                                                                                      | Notes                                                                                                                                                                                                               |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Snapshot diff**   | creation / modification / deletion inside the folder, a replaced folder: **strong**. Only Thumbs.db, desktop.ini or .DS_Store changing, or a rename: **weak** | metadata only: file contents are never opened. A walk that did not finish (budget, unreadable folders) or a missing baseline is **inconclusive**, never "unchanged"                                                 |
| **Usage artifacts** | the shell's _Recent items_ show the folder or a file in it opened after the quiet clock started: **strong**                                                   | Windows. Uses the same snapshot Smart Select already takes                                                                                                                                                          |
| **Access time**     | a file's last-access time newer than the quiet clock: **weak**                                                                                                | only where the file system really maintains access times (the existing reliability probe), never inside Declutterer's own activity windows, never when most files were touched at once (antivirus, backup, indexer) |

Strong use always wins. Unknown (inconclusive) beats weak beats quiet. Weak signals never reset the quiet clock, but a weak signal in the last 14 days holds quiet evidence back.

## How it changes Smart Select's verdicts

New reason codes: _Observed in use_, _Observed unused_, _Observation inconclusive_, _Observed moved or renamed_, _Kept by you_; they appear in the Assessment tooltip like every other reason. All numbers live in `ObservationOptions` (separate from `SmartCleanupOptions`).

- **Observed in use** (state Alive/Retired): junk −3.0, risk 0.5 → demotes an item below Review.
- **Weak signal**: junk −0.6, risk 0.1, and no quiet evidence while it is fresh.
- **Observed unused** (QuietEligible, still valid, enough covered days): junk +1.4 (bounded) and it may relieve up to 50 % of the "this is merely stale" risk of `OrphanedAppData`, `ProjectRoot`, `RecentlyOpened`, `RecentlyModified` – and nothing else. It is applied **only if**:
    - the item is already **Review or Likely without it**, or is **Keep with independent `LongUntouched` or `OrphanedAppData` evidence**. In the latter case, quiet evidence can raise it to **Review at most**; quiet evidence alone cannot promote a Keep;
    - there is no hard keep, credentials, user documents, browser profile, saved games, sensitive location or repository;
    - and it never lifts by more than **one tier**.
      Cloud-synced items get no relief and keep their risk of 0.30 or more.
- **Inconclusive / Unobservable**: a small coverage penalty, no junk evidence.
- **Pinned / snoozed**: a hard keep.

The safety scan, the path validator and every veto stay downstream and unchanged: quiet evidence can never override them.

## Deleting a watched folder

Right before something is deleted, `PreDeleteRevalidator` looks again at every active watch entry equal to or inside it: a fresh fingerprint must match the one from the last check-in. Any difference, an incomplete walk or a blocked path means **that item is not deleted** (it stays in the list, with the reason in the summary) and the entry is marked _in use_. Items without watch history are deleted exactly as before. When the feature is off, nothing is revalidated.

## Background check-ins (optional, opt-in)

`Declutterer --observe` does one check-in without any window and exits (0 = ran or off, 1 = failed, 2 = another check-in held the lock). Registering it with the operating system's scheduler is **never done automatically**: the Watchlist page shows exactly what would be created (the command, the files or task, "runs as you, without administrator rights") and only then adds it; it can be removed from the same page.

| OS      | Mechanism                                                                                                     |
| ------- | ------------------------------------------------------------------------------------------------------------- |
| Windows | Task Scheduler task `\Declutterer\Observation` (daily, current user, least privilege, catches up after sleep) |
| Linux   | systemd user timer `declutterer-observe.timer` (daily, `Persistent=true`)                                     |
| macOS   | launchd agent `com.declutterer.observe` (daily)                                                               |

Only one check-in runs at a time, even across processes (a lock in the ledger that expires by itself if its holder crashes).

## Safety properties, as tests

1. Check-ins never read or change a watched file (`ARunNeverReadsOrChangesTheWatchedFiles_Invariant1`, `Walking_NeverReadsOrChangesAnything_Invariant1`).
2. Every transition is covered by the state-machine table tests.
3. Quiet evidence never promotes a Keep, Safe, vetoed or ineligible item (`ObservationEvidenceTests`, `ObservationEngineScenarioTests`).
4. Unknown is alive or inconclusive, never quiet (truncated walks, crashing sources, lost baselines, poor coverage).
5. Quiet evidence expires, and only counts while valid.
6. Delete-time revalidation (`PreDeleteRevalidatorTests`).
7. A missing, corrupt, locked or newer-schema ledger means no observation evidence and a logged warning – never a crash, never a promotion (`ObservationLedgerTests`).

## Decisions where the brief left room (the conservative reading was taken)

1. **A suspicious Orphaned Roaming app-data folder + a complete quiet window can become Likely, never Safe.** Relief halves its 0.45 risk to 0.225, which Likely tolerates (≤ 0.35) and Safe does not (≤ 0.15). A _one-tier lift cap_ was added as an independent guard. A folder that was Keep before quiet evidence can rise only to Review, and only with independent stale evidence plus a complete safety scan.
2. **Pinned and snoozed items are vetoes** in Smart Select (new reason code `PinnedByUser`), not just "not nominated": otherwise pinning would not keep an item out of a Smart Select run.
3. **Only folders are observed.** A file's metadata says too little, and read-only content is silent by nature.
4. **Source files count as user documents** outside caches and build output, so a project folder with sources is never eligible; the 60-day project class applies to folders that carry project markers but no meaningful content of their own.
5. **"Retroactive lookup"**: use shortly before adding (shell recent items, or the folder's newest content change) starts the item as Alive, if it is within `min(class window, alive cooldown)`; it then behaves exactly like observed use. A recent open only counts as new evidence if it is newer than the quiet clock start _and_ the last strong use (the brief said "newer than enrollment"; that would have kept re-triggering after every reset).
6. **Weak changes are absorbed** into the baseline (shell droppings, a rename) so they are not reported at every check-in.
7. **Delete-time revalidation**: shell-only differences do not block; after a block the entry is Alive and its reference moves to the current state, so a second, deliberate attempt with no further change goes through. Only active entries (Watching, Alive, QuietEligible) are revalidated. When the feature is switched off nothing is revalidated.
8. **Enabling needs the setting to persist**: the ledger is fail-soft, so after writing the switch it is read back; if it did not stick, observation stays off.
9. **A rename is only recognised among the siblings of the old location** (Windows file identity). A move to another folder, and every move on Linux/macOS (path-only identity), reads as _Missing_ – the safe direction.
10. **The reassessment lives in `ObservationReassessor`** (Domain, no UI dependencies) rather than in `SmartCleanupService`, so the headless run can use it. The machine snapshot it takes is the provider's (the Windows one caches for 60 s).
11. Covered-days bookkeeping counts check-in days only (and journal days when a journal provider exists). The ledger lives in the _local_ (not roaming) application data folder.
12. **A pin or snooze protects the neighbourhood, not just the folder.** Only protecting the exact path let Smart Select select a folder *above* a pinned one as a whole (the descendant safety walk and the stage A shortcut know nothing about the ledger, and the pinned folder may not even be loaded in the tree) or pick junk-looking children *inside* it. Every folder that contains a pinned / currently snoozed folder is now a hard keep (`ContainsKeptFolder`, assigned by the engine from `IObservationLookup.ContainsKeptPath`), and the selector and the deepening planner do not look inside a `PinnedByUser` folder. Deleting by hand is unaffected: a pin means "never suggest", not "can not be deleted".

## Not done / not verified

- `IVolumeActivityProvider` (the heartbeat from an NTFS change journal) is wired into the check-in and tested with a simulated source; there is no USN reader, no journal cursor handling and none of the extra Windows artifacts (UserAssist, Prefetch, jump lists), because they need Windows and, for the journal, elevation – and their on-disk formats should be checked against Microsoft's documentation first.
- The Windows-only code (file identity through `GetFileInformationByHandle`, the Task Scheduler XML, `schtasks`) and the systemd / launchd definitions are covered by structural tests of the generated text and by fakes, **not by running them on those systems**.
- The Avalonia pieces are build-checked with the solution; their visual layout has not been separately rendered and reviewed.
