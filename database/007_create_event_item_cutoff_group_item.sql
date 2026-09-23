-- =============================================================================
-- EVENT ITEM CUTOFF GROUP ITEM (THE SELECTION)
-- The items a group closes, selected BY INTERNAL CODE so one entry covers every
-- price point of an item (a division commonly has an early-bird row and a
-- standard row sharing one code).
--
-- The product type is part of the identity because an internal code alone is
-- ambiguous: nothing prevents a division and an event pass sharing one. A group
-- may therefore span product types.
--
-- eicgi_display_name is a SNAPSHOT taken at save time, so a later item rename
-- cannot rewrite the announcement copy or the emails.
--
-- -----------------------------------------------------------------------------
-- WHY THE KEY IS SHAPED THIS WAY
-- -----------------------------------------------------------------------------
-- The natural key is (group, product type, internal code), but it cannot be the
-- primary key on this server. InnoDB limits an index key to 767 BYTES under the
-- Antelope file format with COMPACT rows, and this database is utf8mb4:
--
--   event_item_cutoff_group_id  INT            4 bytes
--   eicgi_product_type          INT            4 bytes
--   eicgi_internal_code         VARCHAR(255)  1020 bytes  (255 x 4)
--                                            ----------
--                                             1028 bytes  ->  ERROR 1071
--
-- Raising the limit to 3072 would mean innodb_file_format=Barracuda AND
-- innodb_large_prefix=ON, both server-wide settings; changing those on a live
-- server for one small table is not a proportionate fix.
--
-- So: a surrogate primary key, and the natural key as a UNIQUE index using a
-- prefix of the code. (767 - 8) / 4 = 189 characters is the maximum; 180 is used
-- for headroom, giving 4 + 4 + 720 = 728 bytes.
--
-- The column stays VARCHAR(255) so it matches the source columns
-- (epass_internal_code and friends) exactly. Shortening it instead would have
-- been simpler, but a code longer than the limit would then be silently
-- truncated and would no longer match its product rows - the cutoff would close
-- nothing and report success.
--
-- The prefix means two codes sharing their first 180 characters would be treated
-- as duplicates and the second silently dropped by INSERT IGNORE. Real internal
-- codes are short identifiers ("JJ-NOV", "14150"), so this is not reachable in
-- practice, and ItemCutoffService already de-duplicates on the FULL code before
-- anything is inserted.
--
-- Writer: Pdc.Mobile REST API, in the same transaction as the group row.
-- =============================================================================

CREATE TABLE IF NOT EXISTS event_item_cutoff_group_item (
	id_event_item_cutoff_group_item INT NOT NULL AUTO_INCREMENT,
	event_item_cutoff_group_id INT NOT NULL,
	eicgi_product_type INT NOT NULL,
	eicgi_internal_code VARCHAR(255) NOT NULL,
	eicgi_display_name VARCHAR(255) NOT NULL,
	PRIMARY KEY (id_event_item_cutoff_group_item),
	-- The natural key. The prefix length is dictated by the 767-byte limit above.
	UNIQUE KEY uq_eicgi_group_type_code (
		event_item_cutoff_group_id,
		eicgi_product_type,
		eicgi_internal_code(180)
	)
-- No explicit charset/collation: inherit the database default (waiver rule).
) ENGINE=InnoDB;
