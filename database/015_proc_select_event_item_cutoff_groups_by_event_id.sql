-- =============================================================================
-- SELECT EVENT ITEM CUTOFF GROUPS BY EVENT ID
-- The list screen's read. Ordered by cutoff ascending so the next deadline is
-- first; the app sinks completed groups to the bottom for display, but the
-- ordering here stays purely chronological so the same result serves the
-- digest email, which reads a day in order.
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_item_cutoff_groups_by_event_id;

DELIMITER $$

CREATE PROCEDURE select_event_item_cutoff_groups_by_event_id(
	IN id_event INT
)
BEGIN
	DECLARE v_id_event INT;

	SET v_id_event = id_event;

	SELECT
		eicg.id_event_item_cutoff_group,
		eicg.event_id_event,
		eicg.eicg_name,
		eicg.eicg_cutoff_local,
		eicg.eicg_time_zone,
		eicg.eicg_cutoff_utc,
		eicg.eicg_enabled,
		eicg.eicg_cutoff_status,
		eicg.eicg_cutoff_attempts,
		eicg.eicg_cutoff_completed_utc,
		eicg.eicg_cutoff_item_count,
		eicg.eicg_cutoff_error,
		eicg.eicg_post_enabled,
		eicg.eicg_post_lead_minutes,
		eicg.eicg_post_send_utc,
		eicg.eicg_post_tone,
		eicg.eicg_post_prompt_hint,
		eicg.eicg_post_title,
		eicg.eicg_post_body,
		eicg.eicg_post_media_json,
		eicg.eicg_post_fingerprint,
		eicg.eicg_post_notify,
		eicg.eicg_post_status,
		eicg.eicg_post_attempts,
		eicg.eicg_post_sent_utc,
		eicg.eicg_post_id,
		eicg.eicg_post_error,
		eicg.eicg_created_by_patron,
		eicg.eicg_created_by_name,
		eicg.eicg_created_utc,
		eicg.eicg_updated_utc
	FROM event_item_cutoff_group eicg
	WHERE eicg.event_id_event = v_id_event
	ORDER BY eicg.eicg_cutoff_utc ASC, eicg.id_event_item_cutoff_group ASC;
END$$

DELIMITER ;
