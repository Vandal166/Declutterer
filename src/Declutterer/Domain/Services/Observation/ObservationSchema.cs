using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

/// <summary>
/// Versioned schema of the observation ledger (PRAGMA user_version). Append a migration to change it; never edit one that shipped.
/// All times are UTC ticks (INTEGER). Only paths and aggregates are stored.
/// </summary>
public static class ObservationSchema
{
    public static readonly string[] Migrations =
    {
        // v1
        $@"
CREATE TABLE watch_item (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path_key TEXT NOT NULL,
    display_path TEXT NOT NULL,
    is_directory INTEGER NOT NULL DEFAULT 1,
    file_id TEXT NULL,
    class INTEGER NOT NULL,
    state INTEGER NOT NULL,
    resolution INTEGER NOT NULL DEFAULT 0,
    enrolled_utc INTEGER NOT NULL,
    quiet_since_utc INTEGER NOT NULL,
    required_days INTEGER NOT NULL,
    covered_days INTEGER NOT NULL DEFAULT 0,
    last_checkin_utc INTEGER NULL,
    last_active_utc INTEGER NULL,
    last_active_strength INTEGER NOT NULL DEFAULT 0,
    last_active_detail TEXT NULL,
    alive_until_utc INTEGER NULL,
    valid_until_utc INTEGER NULL,
    alive_resets INTEGER NOT NULL DEFAULT 0,
    inconclusive_streak INTEGER NOT NULL DEFAULT 0,
    sources INTEGER NOT NULL DEFAULT 0,
    size_at_enrollment INTEGER NOT NULL DEFAULT 0,
    nominated_tier INTEGER NULL,
    nominated_score REAL NULL,
    last_tier INTEGER NULL,
    last_score REAL NULL,
    last_reasons TEXT NULL,
    last_assessed_utc INTEGER NULL,
    pinned INTEGER NOT NULL DEFAULT 0,
    snooze_until_utc INTEGER NULL,
    resolved_utc INTEGER NULL,
    baseline_fingerprint TEXT NULL,
    last_fingerprint TEXT NULL
);
CREATE UNIQUE INDEX ux_watch_item_active_path ON watch_item(path_key) WHERE state <> {(int)WatchState.Resolved};
CREATE INDEX ix_watch_item_state ON watch_item(state);
CREATE TABLE checkin (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    watch_item_id INTEGER NOT NULL REFERENCES watch_item(id) ON DELETE CASCADE,
    utc INTEGER NOT NULL,
    verdict INTEGER NOT NULL,
    source_mask INTEGER NOT NULL,
    covered_days_added INTEGER NOT NULL,
    change_counts TEXT NULL
);
CREATE INDEX ix_checkin_item ON checkin(watch_item_id, utc);
CREATE TABLE covered_day (
    day TEXT PRIMARY KEY,
    heartbeat_source INTEGER NOT NULL
);
CREATE TABLE volume_cursor (
    volume TEXT PRIMARY KEY,
    journal_id INTEGER NOT NULL,
    next_usn INTEGER NOT NULL,
    updated_utc INTEGER NOT NULL
);
CREATE TABLE own_activity (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    start_utc INTEGER NOT NULL,
    end_utc INTEGER NOT NULL,
    kind TEXT NOT NULL
);
CREATE TABLE run_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    started_utc INTEGER NOT NULL,
    finished_utc INTEGER NOT NULL,
    trigger TEXT NOT NULL,
    outcome INTEGER NOT NULL,
    items_checked INTEGER NOT NULL,
    detail TEXT NULL
);
CREATE TABLE settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
",
        // v2
        @"
ALTER TABLE watch_item ADD COLUMN snoozed_at_utc INTEGER NULL;
UPDATE watch_item
SET snoozed_at_utc = COALESCE(last_checkin_utc, quiet_since_utc)
WHERE snooze_until_utc IS NOT NULL;
",
    };

    public static int CurrentVersion => Migrations.Length;
}
