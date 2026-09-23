-- =============================================================================
-- UPDATE EVENT ITEM ADMIN ONLY FLAG
-- Mirrors update_event_item_display_flag exactly, including its type mapping:
-- product types 0 (EventPass) and 4 (Sponsorships) both live in event_passes.
--
-- Setting this to 1 removes the item from the point of sale for non-admin staff
-- (EventItemsTabControl adds an admin-only item only when UserIsAdmin), while
-- leaving an admin able to sell it. Note that a running point of sale station
-- serves from its own in-process cache until its operator clears it.
--
-- Caller: Pdc.Mobile.ItemCutoffs Lambda via
--         Pdc.EventPro UpdateEventItemRepository.UpdateAdminOnlyFlag.
-- =============================================================================

DROP PROCEDURE IF EXISTS update_event_item_admin_only_flag;

DELIMITER $$

CREATE PROCEDURE update_event_item_admin_only_flag(
	IN id_event INT,
	IN item_id INT,
	IN item_type INT,
	IN item_admin_only TINYINT
)
BEGIN
	CASE
		WHEN item_type = 0 OR item_type = 4 THEN
			UPDATE event_passes
			SET epass_admin_only = item_admin_only
			WHERE event_id_event = id_event AND id_event_passes = item_id;
		WHEN item_type = 1 THEN
			UPDATE workshops
			SET wkshop_admin_only = item_admin_only
			WHERE event_id_event = id_event AND id_workshops = item_id;
		WHEN item_type = 2 THEN
			UPDATE merchandise
			SET merch_admin_only = item_admin_only
			WHERE event_id_event = id_event AND id_merchandise = item_id;
		WHEN item_type = 3 THEN
			UPDATE division
			SET division_admin_only = item_admin_only
			WHERE event_id_event = id_event AND id_division = item_id;
	END CASE;
END$$

DELIMITER ;
