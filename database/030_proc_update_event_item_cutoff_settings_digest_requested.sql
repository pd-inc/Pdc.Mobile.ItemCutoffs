-- =============================================================================
-- UPDATE EVENT ITEM CUTOFF SETTINGS DIGEST REQUESTED
-- Sets (API) or clears (Lambda) the on-demand digest request.
--
-- Passing NULL clears it. The row must already exist: an event with no settings
-- row has never been configured, and the API refuses the request rather than
-- creating a settings row as a side effect of pressing Send.
-- =============================================================================

DROP PROCEDURE IF EXISTS update_event_item_cutoff_settings_digest_requested;

DELIMITER $$

CREATE PROCEDURE update_event_item_cutoff_settings_digest_requested(
	IN id_event INT,
	IN eics_digest_requested_for_date DATE
)
BEGIN
	DECLARE v_id_event INT;
	DECLARE v_requested_date DATE;

	SET v_id_event = id_event;
	SET v_requested_date = eics_digest_requested_for_date;

	UPDATE event_item_cutoff_settings eics
	SET
		eics.eics_digest_requested_for_date = v_requested_date,
		eics.eics_updated_utc = UTC_TIMESTAMP()
	WHERE eics.event_id_event = v_id_event;
END$$

DELIMITER ;
