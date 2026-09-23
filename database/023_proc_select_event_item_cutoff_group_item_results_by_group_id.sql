-- =============================================================================
-- SELECT EVENT ITEM CUTOFF GROUP ITEM RESULTS BY GROUP ID
-- What a cutoff actually changed. Answers "why is this item hidden" long after
-- the fact, and is the record a reopen action would restore from if one is ever
-- added (plan D8).
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_item_cutoff_group_item_results_by_group_id;

DELIMITER $$

CREATE PROCEDURE select_event_item_cutoff_group_item_results_by_group_id(
	IN event_item_cutoff_group_id INT
)
BEGIN
	DECLARE v_group_id INT;

	SET v_group_id = event_item_cutoff_group_id;

	SELECT
		eicgir.id_event_item_cutoff_group_item_result,
		eicgir.event_item_cutoff_group_id,
		eicgir.eicgir_product_type,
		eicgir.eicgir_item_id,
		eicgir.eicgir_internal_code,
		eicgir.eicgir_item_name,
		eicgir.eicgir_prior_display,
		eicgir.eicgir_prior_admin_only,
		eicgir.eicgir_prior_sales_closed,
		eicgir.eicgir_applied_utc
	FROM event_item_cutoff_group_item_result eicgir
	WHERE eicgir.event_item_cutoff_group_id = v_group_id
	ORDER BY eicgir.eicgir_product_type ASC, eicgir.eicgir_item_name ASC;
END$$

DELIMITER ;
