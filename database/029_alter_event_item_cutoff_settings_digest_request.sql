-- =============================================================================
-- EVENT ITEM CUTOFF SETTINGS: ON-DEMAND DIGEST REQUEST
-- Lets an admin ask for the day's digest immediately instead of waiting for the
-- scheduled send.
--
-- WHY A COLUMN RATHER THAN THE API JUST SENDING IT
-- The digest has two triggers (the daily schedule and this button), and the
-- cutoff-completed email a third. If the API sent this one itself, the digest
-- RENDERER would have to exist in both the API and the Lambda - and the
-- registration-team wording is exactly the thing that must not drift between two
-- copies (the EventRoleRecipients reasoning, again).
--
-- So the API records the request here and the next tick actions it, within a
-- minute. One renderer, one SMTP configuration, one place the wording lives.
--
-- The date is the EVENT-LOCAL day to report on, so an admin can ask for
-- tomorrow's list as easily as today's.
--
-- Writers: Pdc.Mobile REST API (sets it), Pdc.Mobile.ItemCutoffs (clears it once
-- the attempt completes, success or failure - an admin who sees no email presses
-- the button again, which is more predictable than retrying a bad address on
-- every tick forever).
-- =============================================================================

ALTER TABLE event_item_cutoff_settings
	ADD COLUMN IF NOT EXISTS eics_digest_requested_for_date DATE DEFAULT NULL;
