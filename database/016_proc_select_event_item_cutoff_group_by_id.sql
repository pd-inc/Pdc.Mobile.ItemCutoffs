-- =============================================================================
-- SELECT EVENT ITEM CUTOFF GROUP BY ID
-- One group. The API reads this before an edit to enforce the read-only rule on
-- a completed cutoff, and after a save to return the stored row.
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_item_cutoff_group_by_id;

DELIMITER $$

CREATE PROCEDURE select_event_item_cutoff_group_by_id(
	IN id_event_item_cutoff_group INT
)
BEGIN
	DECLARE v_id INT;

	SET v_id = id_event_item_cutoff_group;

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
	WHERE eicg.id_event_item_cutoff_group = v_id;
END$$

DELIMITER ;
