-- =============================================================================
-- INSERT EVENT ITEM CUTOFF GROUP ITEM RESULT
-- Records one product row's flag values as they were BEFORE the cutoff applied.
-- Written by the executor immediately before it mutates that row, so a failure
-- part way through a group leaves a truthful record of what was already changed.
--
-- The prior values are nullable on purpose: the columns themselves are nullable
-- and NULL is meaningful (never set), so a NULL here means the flag was NULL,
-- not that it was unknown.
-- =============================================================================

DROP PROCEDURE IF EXISTS insert_event_item_cutoff_group_item_result;

DELIMITER $$

CREATE PROCEDURE insert_event_item_cutoff_group_item_result(
	IN event_item_cutoff_group_id INT,
	IN eicgir_product_type INT,
	IN eicgir_item_id INT,
	IN eicgir_internal_code VARCHAR(255),
	IN eicgir_item_name VARCHAR(255),
	IN eicgir_prior_display TINYINT,
	IN eicgir_prior_admin_only TINYINT,
	IN eicgir_prior_sales_closed TINYINT
)
BEGIN
	INSERT INTO event_item_cutoff_group_item_result (
		event_item_cutoff_group_id,
		eicgir_product_type,
		eicgir_item_id,
		eicgir_internal_code,
		eicgir_item_name,
		eicgir_prior_display,
		eicgir_prior_admin_only,
		eicgir_prior_sales_closed,
		eicgir_applied_utc
	)
	VALUES (
		event_item_cutoff_group_id,
		eicgir_product_type,
		eicgir_item_id,
		eicgir_internal_code,
		eicgir_item_name,
		eicgir_prior_display,
		eicgir_prior_admin_only,
		eicgir_prior_sales_closed,
		UTC_TIMESTAMP()
	);
END$$

DELIMITER ;
