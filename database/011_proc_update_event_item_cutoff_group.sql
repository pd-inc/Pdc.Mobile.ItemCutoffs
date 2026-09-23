-- =============================================================================
-- UPDATE EVENT ITEM CUTOFF GROUP
-- Edits a group's configuration. The caller deletes and reinserts the item rows
-- in the same transaction.
--
-- Outcome columns (status, attempts, completed/sent times, errors, post id) are
-- NOT touched here: they belong to the Lambda. Editing a group whose cutoff has
-- already completed is refused by the API, not by this procedure.
--
-- Resetting the post status back to 'pending' when announcements are re-enabled
-- is the API's decision and arrives through eicg_post_status.
-- =============================================================================

DROP PROCEDURE IF EXISTS update_event_item_cutoff_group;

DELIMITER $$

CREATE PROCEDURE update_event_item_cutoff_group(
	IN id_event_item_cutoff_group INT,
	IN eicg_name VARCHAR(100),
	IN eicg_cutoff_local DATETIME,
	IN eicg_time_zone VARCHAR(64),
	IN eicg_cutoff_utc DATETIME,
	IN eicg_enabled TINYINT,
	IN eicg_post_enabled TINYINT,
	IN eicg_post_lead_minutes INT,
	IN eicg_post_send_utc DATETIME,
	IN eicg_post_tone VARCHAR(20),
	IN eicg_post_prompt_hint VARCHAR(500),
	IN eicg_post_title VARCHAR(200),
	IN eicg_post_body VARCHAR(2000),
	IN eicg_post_media_json TEXT,
	IN eicg_post_fingerprint VARCHAR(64),
	IN eicg_post_notify TINYINT,
	IN eicg_post_status VARCHAR(20)
)
BEGIN
	-- Identifier resolution inside a stored program finds a parameter BEFORE a
	-- column of the same name. Every assignment target below is therefore
	-- table-qualified (the column) while the right-hand side is bare (the
	-- parameter), and the WHERE predicate uses a local copy. Removing a
	-- qualifier here would silently assign a column to itself.
	DECLARE v_id INT;

	SET v_id = id_event_item_cutoff_group;

	UPDATE event_item_cutoff_group eicg
	SET
		eicg.eicg_name = eicg_name,
		eicg.eicg_cutoff_local = eicg_cutoff_local,
		eicg.eicg_time_zone = eicg_time_zone,
		eicg.eicg_cutoff_utc = eicg_cutoff_utc,
		eicg.eicg_enabled = eicg_enabled,
		eicg.eicg_post_enabled = eicg_post_enabled,
		eicg.eicg_post_lead_minutes = eicg_post_lead_minutes,
		eicg.eicg_post_send_utc = eicg_post_send_utc,
		eicg.eicg_post_tone = eicg_post_tone,
		eicg.eicg_post_prompt_hint = eicg_post_prompt_hint,
		eicg.eicg_post_title = eicg_post_title,
		eicg.eicg_post_body = eicg_post_body,
		eicg.eicg_post_media_json = eicg_post_media_json,
		eicg.eicg_post_fingerprint = eicg_post_fingerprint,
		eicg.eicg_post_notify = eicg_post_notify,
		eicg.eicg_post_status = eicg_post_status,
		eicg.eicg_updated_utc = UTC_TIMESTAMP()
	WHERE eicg.id_event_item_cutoff_group = v_id;
END$$

DELIMITER ;
