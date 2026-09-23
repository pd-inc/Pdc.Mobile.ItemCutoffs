-- =============================================================================
-- UPDATE EVENT ITEM CUTOFF GROUP POST OUTCOME
-- Records what happened to the announcement. Always increments the attempt
-- count, so a repeatedly failing post eventually drops out of the due query
-- instead of being retried every minute until the cutoff passes.
--
-- Statuses: sent (posted), skipped (the cutoff had already passed when the tick
-- reached it - never post an announcement for a deadline that is gone),
-- failed (attempts exhausted), pending (a retryable failure).
-- =============================================================================

DROP PROCEDURE IF EXISTS update_event_item_cutoff_group_post_outcome;

DELIMITER $$

CREATE PROCEDURE update_event_item_cutoff_group_post_outcome(
	IN id_event_item_cutoff_group INT,
	IN eicg_post_status VARCHAR(20),
	IN eicg_post_id VARCHAR(64),
	IN eicg_post_error VARCHAR(500)
)
BEGIN
	DECLARE v_id INT;
	DECLARE v_status VARCHAR(20);
	DECLARE v_post_id VARCHAR(64);
	DECLARE v_error VARCHAR(500);

	SET v_id = id_event_item_cutoff_group;
	SET v_status = eicg_post_status;
	SET v_post_id = eicg_post_id;
	SET v_error = eicg_post_error;

	UPDATE event_item_cutoff_group eicg
	SET
		eicg.eicg_post_status = v_status,
		eicg.eicg_post_id = COALESCE(v_post_id, eicg.eicg_post_id),
		eicg.eicg_post_error = v_error,
		eicg.eicg_post_attempts = eicg.eicg_post_attempts + 1,
		eicg.eicg_post_sent_utc = CASE WHEN v_status = 'sent' THEN UTC_TIMESTAMP() ELSE eicg.eicg_post_sent_utc END,
		eicg.eicg_updated_utc = UTC_TIMESTAMP()
	WHERE eicg.id_event_item_cutoff_group = v_id;
END$$

DELIMITER ;
