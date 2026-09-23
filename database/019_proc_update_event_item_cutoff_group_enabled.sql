-- =============================================================================
-- UPDATE EVENT ITEM CUTOFF GROUP ENABLED
-- The disable switch. Disabling takes effect on the next tick, because the due
-- query filters on eicg_enabled.
--
-- Re-enabling RESETS BOTH ATTEMPT COUNTS. A group that failed its way out of the
-- due query is otherwise unrecoverable without a database edit, and re-enabling
-- is the operator saying "try this again".
-- =============================================================================

DROP PROCEDURE IF EXISTS update_event_item_cutoff_group_enabled;

DELIMITER $$

CREATE PROCEDURE update_event_item_cutoff_group_enabled(
	IN id_event_item_cutoff_group INT,
	IN eicg_enabled TINYINT
)
BEGIN
	DECLARE v_id INT;
	DECLARE v_enabled TINYINT;

	SET v_id = id_event_item_cutoff_group;
	SET v_enabled = eicg_enabled;

	UPDATE event_item_cutoff_group eicg
	SET
		eicg.eicg_enabled = v_enabled,
		eicg.eicg_cutoff_attempts = CASE WHEN v_enabled = 1 THEN 0 ELSE eicg.eicg_cutoff_attempts END,
		eicg.eicg_post_attempts = CASE WHEN v_enabled = 1 THEN 0 ELSE eicg.eicg_post_attempts END,
		eicg.eicg_updated_utc = UTC_TIMESTAMP()
	WHERE eicg.id_event_item_cutoff_group = v_id;
END$$

DELIMITER ;
