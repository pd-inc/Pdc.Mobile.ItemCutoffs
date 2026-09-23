-- =============================================================================
-- SELECT EVENT ITEMS BY ID: ADD item_sales_closed
--
-- Recreates the existing procedure with one column added. This is the read path
-- behind Pdc.EventPro IItemRepository.FindByEventId, which is used by all three
-- item visibility jobs, by the sales deadline picker, and by the cutoff
-- executor, so the new flag reaches every consumer through this one change.
--
-- Everything else is the production definition verbatim, including the
-- get_item_constrained_price case and the ordering.
--
-- Note on the WHERE clause: the parameter id_event shadows the like-named
-- column, so the unqualified right-hand side resolves to the parameter. That is
-- the existing behaviour and is preserved deliberately.
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_items_by_id;

DELIMITER $$

CREATE PROCEDURE select_event_items_by_id(IN id_event INT)
BEGIN

	SELECT
		view_products.id_event,
		view_products.item_type,
		view_products.item_id,
		view_products.item_name,
		view_products.item_description,
		CASE view_products.item_pricing_method
			WHEN 0 THEN view_products.item_price
			WHEN 1 THEN get_item_constrained_price(view_products.id_event, view_products.item_id, view_products.item_type)
			ELSE view_products.item_price
		END AS item_price,
		'' item_notes,
		view_products.item_pricing_method,
		view_products.item_display,
		view_products.item_internal_code,
		view_products.item_atrib_1_enabled,
		view_products.item_atrib_1_label,
		view_products.item_admin_only,
		view_products.item_division_type,
		view_products.item_display_order,
		view_products.item_seating_enabled,
		view_products.item_recipient_required,
		view_products.item_display_tab,
		view_products.item_start_time,
		view_products.item_end_time,
		view_products.item_is_sponsorship,
		view_products.item_add_to_cart_disabled,
		view_products.item_add_to_cart_disabled_type,
		view_products.id_event_item_tab AS item_tab_id,
		view_products.item_sales_closed
	FROM view_products
	WHERE view_products.id_event = id_event
	ORDER BY item_type, item_internal_code ASC;

END$$

DELIMITER ;
