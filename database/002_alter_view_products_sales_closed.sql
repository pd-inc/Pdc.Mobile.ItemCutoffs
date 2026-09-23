-- =============================================================================
-- VIEW_PRODUCTS: ADD item_sales_closed
--
-- THIS IS THE HIGHEST RISK SCRIPT IN THIS FOLDER. view_products is read by the
-- online store on every event page, by the point of sale, and by all three item
-- visibility jobs. Run it on its own, and verify with the query in README.md
-- before running anything else.
--
-- The change is purely additive: one column appended to each of the four UNION
-- branches, in the same position in every branch. No existing column moves and
-- no predicate changes, so every consumer that selects columns by name is
-- unaffected.
--
-- The definition below is the current production view with that one addition.
-- It deliberately keeps UNION (not UNION ALL), matching the original: switching
-- would change de-duplication behaviour.
--
-- Table names are unqualified so the script applies to whichever schema it is
-- run in (wcs_event_model, _dev, _qa).
-- =============================================================================

CREATE OR REPLACE VIEW view_products AS

	SELECT
		event_passes.event_id_event AS id_event,
		event.event_uuid AS event_uuid,
		event_passes.id_event_passes AS item_id,
		0 AS item_type,
		event_passes.epass_name AS item_name,
		event_passes.epass_description AS item_description,
		'' AS item_location,
		event_passes.epass_price AS item_price,
		event_passes.epass_pricing_method AS item_pricing_method,
		event_passes.epass_display AS item_display,
		event_passes.epass_internal_code AS item_internal_code,
		event_passes.epass_atrib_1_enabled AS item_atrib_1_enabled,
		event_passes.epass_atrib_1_label AS item_atrib_1_label,
		event_passes.epass_admin_only AS item_admin_only,
		event_passes.epass_display_order AS item_display_order,
		event_passes.epass_is_sponsorship AS item_is_sponsorship,
		-(1) AS item_division_type,
		event_passes.epass_seating_enabled AS item_seating_enabled,
		event_passes.epass_recipient_required AS item_recipient_required,
		event_passes.epass_is_sponsorship AS item_display_tab,
		event_passes.epass_start_time AS item_start_time,
		event_passes.epass_end_time AS item_end_time,
		event_passes.epass_add_to_cart_disabled AS item_add_to_cart_disabled,
		event_passes.epass_add_to_cart_disabled_type AS item_add_to_cart_disabled_type,
		event_passes.epass_tab_id AS id_event_item_tab,
		event_passes.epass_sales_closed AS item_sales_closed
	FROM event_passes
		LEFT JOIN event ON (event.id_event = event_passes.event_id_event)

	UNION

	SELECT
		workshops.event_id_event AS id_event,
		event.event_uuid AS event_uuid,
		workshops.id_workshops AS item_id,
		1 AS item_type,
		workshops.wkshop_name AS item_name,
		workshops.wkshop_description AS item_description,
		workshops.wkshop_location AS item_location,
		workshops.wkshop_price AS item_price,
		workshops.wkshop_pricing_method AS item_pricing_method,
		workshops.wkshop_display AS item_display,
		workshops.wkshop_internal_code AS item_internal_code,
		0 AS item_atrib_1_enabled,
		'' AS item_atrib_1_label,
		workshops.wkshop_admin_only AS item_admin_only,
		workshops.wkshop_display_order AS item_display_order,
		0 AS item_is_sponsorship,
		-(1) AS item_division_type,
		0 AS item_seating_enabled,
		1 AS item_recipient_required,
		-(1) AS item_display_tab,
		workshops.wkshop_start_time AS item_start_time,
		workshops.wkshop_end_time AS item_end_time,
		workshops.wkshop_add_to_cart_disabled AS item_add_to_cart_disabled,
		workshops.wkshop_add_to_cart_disabled_type AS item_add_to_cart_disabled_type,
		workshops.wkshop_tab_id AS id_event_item_tab,
		workshops.wkshop_sales_closed AS item_sales_closed
	FROM workshops
		LEFT JOIN event ON (event.id_event = workshops.event_id_event)

	UNION

	SELECT
		merchandise.event_id_event AS id_event,
		event.event_uuid AS event_uuid,
		merchandise.id_merchandise AS item_id,
		2 AS item_type,
		merchandise.merch_name AS item_name,
		merchandise.merch_description AS item_description,
		'' AS item_location,
		merchandise.merch_price AS item_price,
		merchandise.merch_pricing_method AS item_pricing_method,
		merchandise.merch_display AS item_display,
		merchandise.merch_internal_code AS item_internal_code,
		0 AS item_atrib_1_enabled,
		'' AS item_atrib_1_label,
		merchandise.merch_admin_only AS item_admin_only,
		merchandise.merch_display_order AS item_display_order,
		0 AS item_is_sponsorship,
		-(1) AS item_division_type,
		0 AS item_seating_enabled,
		1 AS item_recipient_required,
		-(1) AS item_display_tab,
		merchandise.merch_start_time AS item_start_time,
		merchandise.merch_end_time AS item_end_time,
		merchandise.merch_add_to_cart_disabled AS item_add_to_cart_disabled,
		merchandise.merch_add_to_cart_disabled_type AS item_add_to_cart_disabled_type,
		merchandise.merch_tab_id AS id_event_item_tab,
		merchandise.merch_sales_closed AS item_sales_closed
	FROM merchandise
		LEFT JOIN event ON (event.id_event = merchandise.event_id_event)

	UNION

	SELECT
		division.event_id_event AS id_event,
		event.event_uuid AS event_uuid,
		division.id_division AS item_id,
		3 AS item_type,
		division.division_name AS item_name,
		division.division_description AS item_description,
		division.division_location AS item_location,
		division.division_price AS item_price,
		division.division_pricing_method AS item_pricing_method,
		division.division_display AS item_display,
		division.division_internal_code AS item_internal_code,
		0 AS item_atrib_1_enabled,
		'' AS item_atrib_1_label,
		division.division_admin_only AS item_admin_only,
		division.division_display_order AS item_display_order,
		0 AS item_is_sponsorship,
		division.division_type_id_division_type AS item_division_type,
		0 AS item_seating_enabled,
		1 AS item_recipient_required,
		-(1) AS item_display_tab,
		division.division_start_time AS item_start_time,
		division.division_end_time AS item_end_time,
		division.division_add_to_cart_disabled AS item_add_to_cart_disabled,
		division.division_add_to_cart_disabled_type AS item_add_to_cart_disabled_type,
		division.division_tab_id AS id_event_item_tab,
		division.division_sales_closed AS item_sales_closed
	FROM division
		LEFT JOIN event ON (event.id_event = division.event_id_event);
