-- =============================================================================
-- SELECT EVENT ITEM CUTOFF SETTINGS BY EVENT ID
-- Returns no rows when the event has never been configured; the caller applies
-- the defaults rather than this procedure inventing a row.
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_item_cutoff_settings_by_event_id;

DELIMITER $$

CREATE PROCEDURE select_event_item_cutoff_settings_by_event_id(
	IN id_event INT
)
BEGIN
	DECLARE v_id_event INT;

	SET v_id_event = id_event;

	SELECT
		eics.event_id_event,
		eics.eics_digest_enabled,
		eics.eics_digest_local_time,
		eics.eics_time_zone,
		eics.eics_digest_last_sent_local_date,
		eics.eics_completed_email_enabled,
		eics.eics_updated_utc
	FROM event_item_cutoff_settings eics
	WHERE eics.event_id_event = v_id_event;
END$$

DELIMITER ;
