-- =============================================================================
-- SELECT EVENT ITEM CUTOFF GROUPS DUE
-- The executor's ONLY query on a quiet tick: one indexed range scan across all
-- events that returns nothing almost always.
--
-- A group comes back when it is enabled and either half is due:
--   post_due   - announcements on, still pending, and the send time has passed
--   cutoff_due - cutoff still pending and the cutoff time has passed
-- Both flags are returned so the executor does not re-derive the comparison.
--
-- A group that has exhausted max_attempts on a half is excluded from that half,
-- which is what stops a permanently failing group being retried every minute
-- forever. Re-enabling a group resets its attempt counts.
--
-- now_utc is a parameter rather than UTC_TIMESTAMP() so the console host can
-- evaluate a pass at a simulated time during a dry run.
--
-- The event join supplies the uuid (the RTDB and SQS key) and the name (used in
-- the announcement copy and the emails), so the executor needs no second read.
-- Ordered by cutoff ascending so a backlog after an outage is worked in the
-- order the deadlines were meant to happen.
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_item_cutoff_groups_due;

DELIMITER $$

CREATE PROCEDURE select_event_item_cutoff_groups_due(
	IN now_utc DATETIME,
	IN max_attempts INT
)
BEGIN
	DECLARE v_now DATETIME;
	DECLARE v_max_attempts INT;

	SET v_now = now_utc;
	SET v_max_attempts = max_attempts;

	SELECT
		eicg.id_event_item_cutoff_group,
		eicg.event_id_event,
		event.event_uuid,
		event.event_name,
		eicg.eicg_name,
		eicg.eicg_cutoff_local,
		eicg.eicg_time_zone,
		eicg.eicg_cutoff_utc,
		eicg.eicg_enabled,
		eicg.eicg_cutoff_status,
		eicg.eicg_cutoff_attempts,
		eicg.eicg_post_enabled,
		eicg.eicg_post_lead_minutes,
		eicg.eicg_post_send_utc,
		eicg.eicg_post_title,
		eicg.eicg_post_body,
		eicg.eicg_post_media_json,
		eicg.eicg_post_notify,
		eicg.eicg_post_status,
		eicg.eicg_post_attempts,
		eicg.eicg_created_by_name,
		CASE
			WHEN eicg.eicg_post_enabled = 1
				AND eicg.eicg_post_status = 'pending'
				AND eicg.eicg_post_send_utc IS NOT NULL
				AND eicg.eicg_post_send_utc <= v_now
				AND eicg.eicg_post_attempts < v_max_attempts
			THEN 1 ELSE 0
		END AS post_due,
		CASE
			WHEN eicg.eicg_cutoff_status = 'pending'
				AND eicg.eicg_cutoff_utc <= v_now
				AND eicg.eicg_cutoff_attempts < v_max_attempts
			THEN 1 ELSE 0
		END AS cutoff_due
	FROM event_item_cutoff_group eicg
		INNER JOIN event ON (event.id_event = eicg.event_id_event)
	WHERE eicg.eicg_enabled = 1
		AND (
			(
				eicg.eicg_post_enabled = 1
				AND eicg.eicg_post_status = 'pending'
				AND eicg.eicg_post_send_utc IS NOT NULL
				AND eicg.eicg_post_send_utc <= v_now
				AND eicg.eicg_post_attempts < v_max_attempts
			)
			OR
			(
				eicg.eicg_cutoff_status = 'pending'
				AND eicg.eicg_cutoff_utc <= v_now
				AND eicg.eicg_cutoff_attempts < v_max_attempts
			)
		)
	ORDER BY eicg.eicg_cutoff_utc ASC, eicg.id_event_item_cutoff_group ASC;
END$$

DELIMITER ;
