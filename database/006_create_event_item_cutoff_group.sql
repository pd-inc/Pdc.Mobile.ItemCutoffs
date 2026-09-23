-- =============================================================================
-- EVENT ITEM CUTOFF GROUP (SALES DEADLINE)
-- One named group of event items that closes for sale at a scheduled time, with
-- an optional news feed announcement published a configurable lead beforehand.
--
-- MariaDB is the system of record. The REST API is the only writer of the
-- derived columns (eicg_cutoff_utc, eicg_post_send_utc), recomputed whenever the
-- local time, the zone or the lead changes; the Lambda indexes them so a tick
-- does no time zone arithmetic.
--
-- eicg_cutoff_local is a WALL CLOCK time at the event and eicg_time_zone is its
-- IANA zone. The legacy time_zones offset is a standard-time string and is not
-- safe for arithmetic, which is why the zone is carried here explicitly.
--
-- Writers: Pdc.Mobile REST API (create, edit, enable), Pdc.Mobile.ItemCutoffs
--          Lambda (outcome columns only).
-- =============================================================================

CREATE TABLE IF NOT EXISTS event_item_cutoff_group (
	id_event_item_cutoff_group INT NOT NULL AUTO_INCREMENT,
	event_id_event INT NOT NULL,
	eicg_name VARCHAR(100) NOT NULL,

	-- schedule
	eicg_cutoff_local DATETIME NOT NULL,
	eicg_time_zone VARCHAR(64) NOT NULL,
	eicg_cutoff_utc DATETIME NOT NULL,
	eicg_enabled TINYINT(1) NOT NULL DEFAULT 1,

	-- cutoff outcome
	eicg_cutoff_status VARCHAR(20) NOT NULL DEFAULT 'pending',
	eicg_cutoff_attempts INT NOT NULL DEFAULT 0,
	eicg_cutoff_completed_utc DATETIME DEFAULT NULL,
	eicg_cutoff_item_count INT DEFAULT NULL,
	eicg_cutoff_error VARCHAR(500) DEFAULT NULL,

	-- announcement
	eicg_post_enabled TINYINT(1) NOT NULL DEFAULT 0,
	eicg_post_lead_minutes INT DEFAULT NULL,
	eicg_post_send_utc DATETIME DEFAULT NULL,
	eicg_post_tone VARCHAR(20) DEFAULT NULL,
	eicg_post_prompt_hint VARCHAR(500) DEFAULT NULL,
	eicg_post_title VARCHAR(200) DEFAULT NULL,
	eicg_post_body VARCHAR(2000) DEFAULT NULL,
	eicg_post_media_json TEXT,
	eicg_post_fingerprint VARCHAR(64) DEFAULT NULL,
	eicg_post_notify TINYINT(1) NOT NULL DEFAULT 1,

	-- announcement outcome
	eicg_post_status VARCHAR(20) NOT NULL DEFAULT 'none',
	eicg_post_attempts INT NOT NULL DEFAULT 0,
	eicg_post_sent_utc DATETIME DEFAULT NULL,
	eicg_post_id VARCHAR(64) DEFAULT NULL,
	eicg_post_error VARCHAR(500) DEFAULT NULL,

	-- audit
	eicg_created_by_patron INT NOT NULL,
	eicg_created_by_name VARCHAR(255) NOT NULL,
	eicg_created_utc DATETIME NOT NULL,
	eicg_updated_utc DATETIME NOT NULL,

	PRIMARY KEY (id_event_item_cutoff_group),
	KEY idx_eicg_due_cutoff (eicg_enabled, eicg_cutoff_status, eicg_cutoff_utc),
	KEY idx_eicg_due_post (eicg_enabled, eicg_post_status, eicg_post_send_utc),
	KEY idx_eicg_event (event_id_event, eicg_cutoff_utc)
-- No explicit charset/collation: inherit the database default (waiver rule).
) ENGINE=InnoDB;
