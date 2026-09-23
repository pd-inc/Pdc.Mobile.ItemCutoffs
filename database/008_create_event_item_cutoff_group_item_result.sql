-- =============================================================================
-- EVENT ITEM CUTOFF GROUP ITEM RESULT (WHAT ACTUALLY CHANGED)
-- One row per product row a cutoff touched, carrying the flag values that were
-- in place BEFORE the cutoff applied.
--
-- This is the audit trail that answers "why is this item hidden" long after the
-- fact, and it is the exact record a reopen action would restore from. Reopen is
-- deliberately out of scope for now (plan D8); the record costs nothing to write
-- and is what makes adding it later small rather than a guess.
--
-- Writer: Pdc.Mobile.ItemCutoffs Lambda, before mutating each row.
-- =============================================================================

CREATE TABLE IF NOT EXISTS event_item_cutoff_group_item_result (
	id_event_item_cutoff_group_item_result INT NOT NULL AUTO_INCREMENT,
	event_item_cutoff_group_id INT NOT NULL,
	eicgir_product_type INT NOT NULL,
	eicgir_item_id INT NOT NULL,
	eicgir_internal_code VARCHAR(255) NOT NULL,
	eicgir_item_name VARCHAR(255) NOT NULL,
	eicgir_prior_display TINYINT(1) DEFAULT NULL,
	eicgir_prior_admin_only TINYINT(1) DEFAULT NULL,
	eicgir_prior_sales_closed TINYINT(1) DEFAULT NULL,
	eicgir_applied_utc DATETIME NOT NULL,
	PRIMARY KEY (id_event_item_cutoff_group_item_result),
	KEY idx_eicgir_group (event_item_cutoff_group_id)
) ENGINE=InnoDB;
