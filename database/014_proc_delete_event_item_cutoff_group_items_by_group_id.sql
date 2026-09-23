-- =============================================================================
-- DELETE EVENT ITEM CUTOFF GROUP ITEMS BY GROUP ID
-- Clears a group's selection so the caller can reinsert it. An edit is a
-- delete-and-reinsert inside the caller's transaction rather than a diff,
-- because the selection is small and a diff has no advantage here.
-- =============================================================================

DROP PROCEDURE IF EXISTS delete_event_item_cutoff_group_items_by_group_id;

DELIMITER $$

CREATE PROCEDURE delete_event_item_cutoff_group_items_by_group_id(
	IN event_item_cutoff_group_id INT
)
BEGIN
	DECLARE v_group_id INT;

	SET v_group_id = event_item_cutoff_group_id;

	DELETE eicgi FROM event_item_cutoff_group_item eicgi
	WHERE eicgi.event_item_cutoff_group_id = v_group_id;
END$$

DELIMITER ;
