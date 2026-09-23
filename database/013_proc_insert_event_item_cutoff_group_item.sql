-- =============================================================================
-- INSERT EVENT ITEM CUTOFF GROUP ITEM
-- One selected internal code, within a product type.
--
-- eicgi_display_name is a snapshot of the item's name at save time, so the
-- announcement copy and the emails cannot be rewritten by a later rename.
--
-- INSERT IGNORE on the primary key, so a duplicate code in the submitted
-- selection is absorbed rather than failing the whole save.
-- =============================================================================

DROP PROCEDURE IF EXISTS insert_event_item_cutoff_group_item;

DELIMITER $$

CREATE PROCEDURE insert_event_item_cutoff_group_item(
	IN event_item_cutoff_group_id INT,
	IN eicgi_product_type INT,
	IN eicgi_internal_code VARCHAR(255),
	IN eicgi_display_name VARCHAR(255)
)
BEGIN
	INSERT IGNORE INTO event_item_cutoff_group_item (
		event_item_cutoff_group_id,
		eicgi_product_type,
		eicgi_internal_code,
		eicgi_display_name
	)
	VALUES (
		event_item_cutoff_group_id,
		eicgi_product_type,
		eicgi_internal_code,
		eicgi_display_name
	);
END$$

DELIMITER ;
