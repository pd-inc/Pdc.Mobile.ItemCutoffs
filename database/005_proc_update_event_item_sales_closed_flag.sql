-- =============================================================================
-- UPDATE EVENT ITEM SALES CLOSED FLAG
-- Same shape and type mapping as update_event_item_display_flag.
--
-- Setting this to 1 tells the three item visibility jobs in
-- Pdc.Ecommerce.ScheduledTasks never to turn the item back on. It changes
-- nothing else: the store filters on item_display and the point of sale on
-- item_admin_only, so no purchasing path reads this column.
--
-- Caller: Pdc.Mobile.ItemCutoffs Lambda via
--         Pdc.EventPro UpdateEventItemRepository.UpdateSalesClosedFlag.
-- =============================================================================

DROP PROCEDURE IF EXISTS update_event_item_sales_closed_flag;

DELIMITER $$

CREATE PROCEDURE update_event_item_sales_closed_flag(
	IN id_event INT,
	IN item_id INT,
	IN item_type INT,
	IN item_sales_closed TINYINT
)
BEGIN
	CASE
		WHEN item_type = 0 OR item_type = 4 THEN
			UPDATE event_passes
			SET epass_sales_closed = item_sales_closed
			WHERE event_id_event = id_event AND id_event_passes = item_id;
		WHEN item_type = 1 THEN
			UPDATE workshops
			SET wkshop_sales_closed = item_sales_closed
			WHERE event_id_event = id_event AND id_workshops = item_id;
		WHEN item_type = 2 THEN
			UPDATE merchandise
			SET merch_sales_closed = item_sales_closed
			WHERE event_id_event = id_event AND id_merchandise = item_id;
		WHEN item_type = 3 THEN
			UPDATE division
			SET division_sales_closed = item_sales_closed
			WHERE event_id_event = id_event AND id_division = item_id;
	END CASE;
END$$

DELIMITER ;
