-- =============================================================================
-- EVENT ITEM CUTOFF SETTINGS (PER EVENT EMAIL CONFIGURATION)
-- Whether the daily digest and the cutoff-completed email are sent for an event,
-- and when the digest goes out.
--
-- Recipients are NOT stored here. They are resolved at send time from
-- patron_event_roles against a hardcoded role set that lives once in
-- Pdc.EventPro (EventRoleRecipients.DefaultRoleNames), so a staff change the day
-- before is picked up with no action and there is no address to mistype.
--
-- eics_digest_last_sent_local_date is the guard that stops a retried tick
-- sending the digest twice in one day.
--
-- Writers: Pdc.Mobile REST API (settings), Pdc.Mobile.ItemCutoffs Lambda
--          (the last-sent date only).
-- =============================================================================

CREATE TABLE IF NOT EXISTS event_item_cutoff_settings (
	event_id_event INT NOT NULL,
	eics_digest_enabled TINYINT(1) NOT NULL DEFAULT 0,
	eics_digest_local_time TIME NOT NULL DEFAULT '08:00:00',
	eics_time_zone VARCHAR(64) NOT NULL,
	eics_digest_last_sent_local_date DATE DEFAULT NULL,
	eics_completed_email_enabled TINYINT(1) NOT NULL DEFAULT 1,
	eics_updated_utc DATETIME NOT NULL,
	PRIMARY KEY (event_id_event)
) ENGINE=InnoDB;
