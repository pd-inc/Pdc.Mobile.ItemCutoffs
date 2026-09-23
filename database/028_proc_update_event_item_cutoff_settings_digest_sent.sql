-- =============================================================================
-- UPDATE EVENT ITEM CUTOFF SETTINGS DIGEST SENT
-- Stamps the event-local date the digest was last sent for.
--
-- This is the guard that stops a retried or overlapping tick sending the digest
-- twice in one day. It is written AFTER the send succeeds, so a failed send is
-- retried on the next tick rather than being silently skipped.
--
-- The date is the event's LOCAL date, not UTC, because the digest is "today's
-- closures at the event" and an evening send time would otherwise roll over a
-- day early or late depending on the zone.
-- =============================================================================

DROP PROCEDURE IF EXISTS update_event_item_cutoff_settings_digest_sent;

DELIMITER $$

CREATE PROCEDURE update_event_item_cutoff_settings_digest_sent(
	IN id_event INT,
	IN eics_digest_last_sent_local_date DATE
)
BEGIN
	DECLARE v_id_event INT;
	DECLARE v_sent_date DATE;

	SET v_id_event = id_event;
	SET v_sent_date = eics_digest_last_sent_local_date;

	UPDATE event_item_cutoff_settings eics
	SET
		eics.eics_digest_last_sent_local_date = v_sent_date,
		eics.eics_updated_utc = UTC_TIMESTAMP()
	WHERE eics.event_id_event = v_id_event;
END$$

DELIMITER ;
