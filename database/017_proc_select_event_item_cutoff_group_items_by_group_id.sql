-- =============================================================================
-- SELECT EVENT ITEM CUTOFF GROUP ITEMS BY GROUP ID
-- A group's selection, ordered so the announcement bullets and the digest email
-- list items in a stable order across runs.
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_item_cutoff_group_items_by_group_id;

DELIMITER $$

CREATE PROCEDURE select_event_item_cutoff_group_items_by_group_id(
	IN event_item_cutoff_group_id INT
)
BEGIN
	DECLARE v_group_id INT;

	SET v_group_id = event_item_cutoff_group_id;

	SELECT
		eicgi.event_item_cutoff_group_id,
		eicgi.eicgi_product_type,
		eicgi.eicgi_internal_code,
		eicgi.eicgi_display_name
	FROM event_item_cutoff_group_item eicgi
	WHERE eicgi.event_item_cutoff_group_id = v_group_id
	ORDER BY eicgi.eicgi_product_type ASC, eicgi.eicgi_display_name ASC;
END$$

DELIMITER ;
