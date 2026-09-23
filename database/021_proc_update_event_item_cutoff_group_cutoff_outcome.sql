-- =============================================================================
-- UPDATE EVENT ITEM CUTOFF GROUP CUTOFF OUTCOME
-- Records what happened to the cutoff. Always increments the attempt count.
--
-- Statuses: completed (items closed), failed (attempts exhausted), pending (a
-- retryable failure - the next tick tries again, and a late cutoff still runs
-- because late is better than never).
--
-- eicg_cutoff_item_count is the number of PRODUCT ROWS changed, not the number
-- of internal codes selected, so a group of two codes covering four price points
-- reports 4.
-- =============================================================================

DROP PROCEDURE IF EXISTS update_event_item_cutoff_group_cutoff_outcome;

DELIMITER $$

CREATE PROCEDURE update_event_item_cutoff_group_cutoff_outcome(
	IN id_event_item_cutoff_group INT,
	IN eicg_cutoff_status VARCHAR(20),
	IN eicg_cutoff_item_count INT,
	IN eicg_cutoff_error VARCHAR(500)
)
BEGIN
	DECLARE v_id INT;
	DECLARE v_status VARCHAR(20);
	DECLARE v_item_count INT;
	DECLARE v_error VARCHAR(500);

	SET v_id = id_event_item_cutoff_group;
	SET v_status = eicg_cutoff_status;
	SET v_item_count = eicg_cutoff_item_count;
	SET v_error = eicg_cutoff_error;

	UPDATE event_item_cutoff_group eicg
	SET
		eicg.eicg_cutoff_status = v_status,
		eicg.eicg_cutoff_item_count = COALESCE(v_item_count, eicg.eicg_cutoff_item_count),
		eicg.eicg_cutoff_error = v_error,
		eicg.eicg_cutoff_attempts = eicg.eicg_cutoff_attempts + 1,
		eicg.eicg_cutoff_completed_utc = CASE WHEN v_status = 'completed' THEN UTC_TIMESTAMP() ELSE eicg.eicg_cutoff_completed_utc END,
		eicg.eicg_updated_utc = UTC_TIMESTAMP()
	WHERE eicg.id_event_item_cutoff_group = v_id;
END$$

DELIMITER ;
