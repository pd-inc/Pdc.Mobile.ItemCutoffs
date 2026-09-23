-- =============================================================================
-- UPSERT EVENT ITEM CUTOFF SETTINGS
-- Per-event email configuration. An event has no row until an admin opens the
-- settings screen and saves, so every reader treats a missing row as the
-- defaults (digest off, completed email on).
--
-- eics_digest_last_sent_local_date is deliberately NOT touched here: it belongs
-- to the Lambda, and clearing it on a settings save would let the digest go out
-- twice in one day after an admin changed the send time.
-- =============================================================================

DROP PROCEDURE IF EXISTS upsert_event_item_cutoff_settings;

DELIMITER $$

CREATE PROCEDURE upsert_event_item_cutoff_settings(
	IN id_event INT,
	IN eics_digest_enabled TINYINT,
	IN eics_digest_local_time TIME,
	IN eics_time_zone VARCHAR(64),
	IN eics_completed_email_enabled TINYINT
)
BEGIN
	INSERT INTO event_item_cutoff_settings (
		event_id_event,
		eics_digest_enabled,
		eics_digest_local_time,
		eics_time_zone,
		eics_completed_email_enabled,
		eics_updated_utc
	)
	VALUES (
		id_event,
		eics_digest_enabled,
		eics_digest_local_time,
		eics_time_zone,
		eics_completed_email_enabled,
		UTC_TIMESTAMP()
	)
	ON DUPLICATE KEY UPDATE
		eics_digest_enabled = VALUES(eics_digest_enabled),
		eics_digest_local_time = VALUES(eics_digest_local_time),
		eics_time_zone = VALUES(eics_time_zone),
		eics_completed_email_enabled = VALUES(eics_completed_email_enabled),
		eics_updated_utc = UTC_TIMESTAMP();
END$$

DELIMITER ;
