-- =============================================================================
-- SALES CLOSED FLAG ON THE FOUR PRODUCT TABLES
-- Marks an item as closed by a sales deadline. Its ONLY consumers are the three
-- item visibility jobs in Pdc.Ecommerce.ScheduledTasks, which must never turn a
-- flagged item back on. The store and the point of sale are unaffected: the
-- store already filters item_display = 1 and the point of sale already hides
-- admin-only items from non-admin staff.
--
-- Only an explicit 1 denies. NULL (never set) and 0 allow, matching the
-- event_mobile_app_enabled convention, so the column is safe to add to live
-- tables with no backfill.
--
-- Writers: Pdc.Mobile.ItemCutoffs Lambda (via update_event_item_sales_closed_flag),
--          and an operator closing items by hand.
-- =============================================================================

ALTER TABLE event_passes ADD COLUMN IF NOT EXISTS epass_sales_closed TINYINT(1) DEFAULT NULL;

ALTER TABLE workshops ADD COLUMN IF NOT EXISTS wkshop_sales_closed TINYINT(1) DEFAULT NULL;

ALTER TABLE merchandise ADD COLUMN IF NOT EXISTS merch_sales_closed TINYINT(1) DEFAULT NULL;

ALTER TABLE division ADD COLUMN IF NOT EXISTS division_sales_closed TINYINT(1) DEFAULT NULL;
