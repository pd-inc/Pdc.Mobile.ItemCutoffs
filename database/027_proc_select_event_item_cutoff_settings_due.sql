-- =============================================================================
-- SELECT EVENT ITEM CUTOFF SETTINGS DUE
-- Candidate events whose daily digest may be due on this tick.
--
-- The real decision is made by the caller, not here. The configured send time is
-- a WALL CLOCK time in the event's IANA zone, and MariaDB 10.1 has no reliable
-- IANA conversion available to it, so the executor applies the zone with
-- TimeZoneInfo, works out the event's local date and local time, and sends only
-- when the local time has passed the configured one and the local date is newer
-- than eics_digest_last_sent_local_date.
--
-- This procedure narrows the set the executor has to convert. A row cannot
-- possibly be due when it has already been sent for a date at or beyond the
-- LATEST local date anywhere on earth, which is DATE(now_utc + 14 hours) for
-- UTC+14. Anything else is a candidate.
--
-- Keeping the bound generous is deliberate: a too-clever filter here would skip
-- a real send, and the cost of an extra candidate is one TimeZoneInfo lookup.
--
-- The event join supplies the uuid the recipient lookup needs.
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_item_cutoff_settings_due;

DELIMITER $$

CREATE PROCEDURE select_event_item_cutoff_settings_due(
	IN now_utc DATETIME
)
BEGIN
	DECLARE v_now DATETIME;

	SET v_now = now_utc;

	SELECT
		eics.event_id_event,
		event.event_uuid,
		event.event_name,
		eics.eics_digest_enabled,
		eics.eics_digest_local_time,
		eics.eics_time_zone,
		eics.eics_digest_last_sent_local_date,
		eics.eics_digest_requested_for_date,
		eics.eics_completed_email_enabled,
		eics.eics_updated_utc
	FROM event_item_cutoff_settings eics
		INNER JOIN event ON (event.id_event = eics.event_id_event)
	WHERE
		-- An on-demand request is actioned whatever the schedule says. An event
		-- that wants no daily mail but wants today's list is a real case, so this
		-- deliberately ignores both eics_digest_enabled and the last-sent guard.
		eics.eics_digest_requested_for_date IS NOT NULL
		OR (
			eics.eics_digest_enabled = 1
			AND (
				eics.eics_digest_last_sent_local_date IS NULL
				OR eics.eics_digest_last_sent_local_date < DATE(DATE_ADD(v_now, INTERVAL 14 HOUR))
			)
		)
	ORDER BY eics.event_id_event ASC;
END$$

DELIMITER ;
