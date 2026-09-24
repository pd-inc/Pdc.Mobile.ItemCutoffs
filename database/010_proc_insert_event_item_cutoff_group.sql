-- =============================================================================
-- INSERT EVENT ITEM CUTOFF GROUP
-- Creates the group row and returns its id through an OUT parameter.
--
-- The caller (Pdc.Mobile REST API) runs this and insert_event_item_cutoff_group_item
-- on ONE connection inside ONE transaction, so a group is never stored without
-- its items. Items are not passed as a delimited list because internal codes are
-- free-text varchar(255) and any delimiter could collide with real data.
--
-- The derived columns eicg_cutoff_utc and eicg_post_send_utc are computed by the
-- API from the wall-clock time, the IANA zone and the lead; this procedure
-- stores what it is given and never derives them itself.
-- =============================================================================

DROP PROCEDURE IF EXISTS insert_event_item_cutoff_group;

DELIMITER $$

CREATE PROCEDURE insert_event_item_cutoff_group(
	IN id_event INT,
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
	IN eicg_post_status VARCHAR(20),
	IN eicg_created_by_patron INT,
	IN eicg_created_by_name VARCHAR(255),
	OUT last_inserted_id INT
)
BEGIN
	INSERT INTO event_item_cutoff_group (
		event_id_event,
		eicg_name,
		eicg_cutoff_local,
		eicg_time_zone,
		eicg_cutoff_utc,
		eicg_enabled,
		eicg_cutoff_status,
		eicg_post_enabled,
		eicg_post_lead_minutes,
		eicg_post_send_utc,
		eicg_post_tone,
		eicg_post_prompt_hint,
		eicg_post_title,
		eicg_post_body,
		eicg_post_media_json,
		eicg_post_fingerprint,
		eicg_post_notify,
		eicg_post_status,
		eicg_created_by_patron,
		eicg_created_by_name,
		eicg_created_utc,
		eicg_updated_utc
	)
	VALUES (
		id_event,
		eicg_name,
		eicg_cutoff_local,
		eicg_time_zone,
		eicg_cutoff_utc,
		eicg_enabled,
		'pending',
		eicg_post_enabled,
		eicg_post_lead_minutes,
		eicg_post_send_utc,
		eicg_post_tone,
		eicg_post_prompt_hint,
		eicg_post_title,
		eicg_post_body,
		eicg_post_media_json,
		eicg_post_fingerprint,
		eicg_post_notify,
		eicg_post_status,
		eicg_created_by_patron,
		eicg_created_by_name,
		UTC_TIMESTAMP(),
		UTC_TIMESTAMP()
	);

	SET last_inserted_id = LAST_INSERT_ID();
END$$

DELIMITER ;
