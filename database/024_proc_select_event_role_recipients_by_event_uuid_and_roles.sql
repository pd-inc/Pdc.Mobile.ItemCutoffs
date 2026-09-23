-- =============================================================================
-- SELECT EVENT ROLE RECIPIENTS BY EVENT UUID AND ROLES
-- The email recipients for an event: the people holding one of the given roles.
--
-- WHY THIS EXISTS RATHER THAN REUSING select_event_users_by_role_and_event_id:
-- that procedure INNER JOINs mobile_app_device_tokens because it was written for
-- push targeting. Using it for email would silently drop every role holder who
-- never installed the app, which is exactly the person most likely to be missed.
-- This is the same query without that join.
--
-- Filters, each deliberate:
--   - patron_event_role_enabled = true. patron_event_role_expires is IGNORED,
--     matching select_event_users_by_role_and_event_id and
--     select_patron_event_roles_by_event_id; honouring it here alone would make
--     role reads inconsistent across the system.
--   - a non-empty email.
--   - the account-deletion tombstone (deleted-{id}@deleted.invalid) is excluded.
--     This is the filter rather than patron_disabled because that column is
--     applied manually per environment and is not present everywhere.
--   - patron_email_address_optout is NOT honoured: it is a marketing opt-out,
--     and these are operational emails the event's own roles subscribe you to.
--
-- DISTINCT plus one row per (patron, role): a patron holding two of the roles
-- appears twice, and the caller dedupes by address while keeping the role names
-- for display.
--
-- Caller: Pdc.Mobile REST API and Pdc.Mobile.ItemCutoffs Lambda via
--         Pdc.EventPro FindEventUsersByRoleRepository.FindRecipients.
-- =============================================================================

DROP PROCEDURE IF EXISTS select_event_role_recipients_by_event_uuid_and_roles;

DELIMITER $$

CREATE PROCEDURE select_event_role_recipients_by_event_uuid_and_roles(
	IN event_uuid VARCHAR(36),
	IN roles VARCHAR(500)
)
BEGIN
	DECLARE v_event_uuid VARCHAR(36);
	DECLARE v_roles VARCHAR(500);

	SET v_event_uuid = event_uuid;
	SET v_roles = roles;

	SELECT DISTINCT
		event.id_event,
		event.event_name,
		patron.id_patron,
		patron.patron_uuid,
		patron.patron_first_name,
		patron.patron_last_name,
		patron.patron_email,
		role.id_role,
		role.role_name,
		role.role_display_name,
		patron_event_roles.patron_event_role_enabled,
		patron_event_roles.patron_event_role_is_primary
	FROM event
		INNER JOIN patron_event_roles
			ON (event.id_event = patron_event_roles.event_id_event)
		INNER JOIN role
			ON (patron_event_roles.role_id_role = role.id_role)
		INNER JOIN patron
			ON (patron.id_patron = patron_event_roles.patron_id_patron)
	WHERE event.event_uuid = v_event_uuid
		AND patron_event_roles.patron_event_role_enabled = TRUE
		AND FIND_IN_SET(role.role_name, v_roles)
		AND patron.patron_email IS NOT NULL
		AND TRIM(patron.patron_email) <> ''
		AND patron.patron_email NOT LIKE '%@deleted.invalid'
	ORDER BY patron.patron_last_name ASC, patron.patron_first_name ASC, role.role_display_name ASC;
END$$

DELIMITER ;
