-- =============================================================================
-- DELETE EVENT ITEM CUTOFF GROUP
-- Removes the group and everything hanging off it, in one transaction: the
-- selection, the applied-result audit rows, then the group.
--
-- Deleting a completed group discards its audit trail. That is accepted: the
-- group row is the only index of those result rows, and a completed cutoff has
-- already been reported by email and logged with a per-group Information line
-- that survives independently in Elasticsearch.
-- =============================================================================

DROP PROCEDURE IF EXISTS delete_event_item_cutoff_group;

DELIMITER $$

CREATE PROCEDURE delete_event_item_cutoff_group(
	IN id_event_item_cutoff_group INT
)
BEGIN
	DECLARE v_id INT;

	SET v_id = id_event_item_cutoff_group;

	START TRANSACTION;

	DELETE FROM event_item_cutoff_group_item_result
	WHERE event_item_cutoff_group_id = v_id;

	DELETE FROM event_item_cutoff_group_item
	WHERE event_item_cutoff_group_id = v_id;

	DELETE eicg FROM event_item_cutoff_group eicg
	WHERE eicg.id_event_item_cutoff_group = v_id;

	COMMIT;
END$$

DELIMITER ;
