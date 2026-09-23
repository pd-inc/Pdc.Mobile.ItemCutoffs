# Sales Deadlines - Database Objects

Schema and stored procedures for the sales deadline feature, targeting the WDR ecommerce MariaDB (`wcs_event_model*`).

Scripts are **idempotent** (`ADD COLUMN IF NOT EXISTS`, `CREATE OR REPLACE VIEW`, `CREATE TABLE IF NOT EXISTS`, `DROP PROCEDURE IF EXISTS` + `CREATE`).
Run them **manually, in numeric order, once per environment** (dev, then prod), before the code that calls them is deployed.

The server is MariaDB 10.1, so there are no JSON functions and no common table expressions anywhere in these scripts.

## Run order and what each script does

### Stage 1 - the shared item schema (scripts 001 to 005)

These touch objects the online store, the point of sale, and the three item visibility jobs already use.
Run them first, verify, and only then continue.

| Script | Object | Notes |
|---|---|---|
| `001_alter_product_tables_sales_closed.sql` | `epass_sales_closed`, `wkshop_sales_closed`, `merch_sales_closed`, `division_sales_closed` | Additive nullable columns. Only an explicit `1` denies; NULL and `0` allow |
| `002_alter_view_products_sales_closed.sql` | `view_products` | **The highest risk script here.** Run it on its own and verify before continuing |
| `003_proc_select_event_items_by_id.sql` | `select_event_items_by_id` | Adds `item_sales_closed` to the read path behind `IItemRepository.FindByEventId` |
| `004_proc_update_event_item_admin_only_flag.sql` | `update_event_item_admin_only_flag` | Mirrors `update_event_item_display_flag`, including type 4 mapping to `event_passes` |
| `005_proc_update_event_item_sales_closed_flag.sql` | `update_event_item_sales_closed_flag` | Same shape |

**Why `view_products` is the risky one.** It is read by the store on every event page, by the point of sale, and by all three visibility jobs.
The change is purely additive: one column appended to each of the four `UNION` branches, in the same position in every branch, with no existing column moved and no predicate changed.
Script 002 carries the current production definition with that one addition, and deliberately keeps `UNION` rather than `UNION ALL`, which would change de-duplication behaviour.

Verify immediately after running 002:

```bash
~/.claude/skills/mariadb-ddl/scripts/ddl query "SELECT COLUMN_NAME, ORDINAL_POSITION FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = 'wcs_event_model_dev' AND TABLE_NAME = 'view_products' ORDER BY ORDINAL_POSITION"
```

Expect the pre-existing columns in their original order with `item_sales_closed` last.
If the column count or order differs from the production view this script was written against, **stop**: the view has been changed elsewhere since, and script 002 needs rebasing onto the current definition rather than being forced through.

### Stage 2 - the feature's own tables (scripts 006 to 009)

| Script | Object | Notes |
|---|---|---|
| `006_create_event_item_cutoff_group.sql` | `event_item_cutoff_group` | One sales deadline: schedule, announcement, and both outcomes |
| `007_create_event_item_cutoff_group_item.sql` | `event_item_cutoff_group_item` | The selection, by product type and internal code. Surrogate PK + prefixed unique index; see "Server constraints" |
| `008_create_event_item_cutoff_group_item_result.sql` | `event_item_cutoff_group_item_result` | What actually changed, with the prior flag values |
| `009_create_event_item_cutoff_settings.sql` | `event_item_cutoff_settings` | Per-event email configuration |

### Stage 3 - the feature's procedures (scripts 010 to 028)

Groups (`010` to `012`), selection (`013`, `014`), reads (`015` to `017`), the executor's due query (`018`), the enable switch (`019`), outcomes (`020`, `021`), the applied-result audit (`022`, `023`), email recipients (`024`), and settings (`025` to `028`).

## Server constraints that shaped the schema

This server is **MariaDB 10.1** with `innodb_file_format = Antelope`, `innodb_large_prefix = OFF` and `innodb_default_row_format = compact`, and the database default charset is **utf8mb4** (4 bytes per character).

That fixes the InnoDB index key limit at **767 bytes**, which is why `event_item_cutoff_group_item` has a surrogate primary key and a prefixed unique index rather than the natural key it wants:

```
event_item_cutoff_group_id  INT            4 bytes
eicgi_product_type          INT            4 bytes
eicgi_internal_code         VARCHAR(255)  1020 bytes   (255 x 4)
                                         ----------
                                          1028 bytes   ->  ERROR 1071
```

The maximum usable prefix is `(767 - 8) / 4 = 189` characters; the script uses 180. Script 007 carries the full reasoning, including why the column was **not** simply shortened (a truncated code no longer matches its product rows, so the cutoff would close nothing and report success).

Raising the limit to 3072 bytes would need `innodb_file_format = Barracuda` **and** `innodb_large_prefix = ON`, both server-wide. Do not change those for this feature.

Check before adding any index to these tables:

```bash
~/.claude/skills/mariadb-ddl/scripts/ddl query "SHOW VARIABLES LIKE 'innodb_large_prefix'"
```

There are no other long-string indexes in this folder: every other key is over `INT` columns.

## Conventions

Shared with `Pdc.Mobile.Waiver/database/` and `Pdc.Mobile.AttendeeSync/database/`:

- Stored procedure **parameter names match the wire names** used by `Pdc.EventPro.Repositories.MySql` (`SqlTokens.*`), because MySql.Data binds parameters by declared name.
- A stored-procedure **parameter shadows a column of the same name**, so every predicate **table-qualifies the column** and uses a `v_` local for the value. In an `UPDATE`, the assignment target is table-qualified (the column) and the right-hand side is bare (the parameter); removing a qualifier there would silently assign a column to itself.
- Tables declare **no explicit charset or collation**; they inherit the database default.
- Tables are `InnoDB`. The legacy product tables are MyISAM and are not converted.

## Rules that are load bearing

**Deploy `Pdc.Ecommerce.ScheduledTasks` before anything sets `sales_closed`.**
That service is what honours the flag.
If the executor runs first, the next daily `item-visibility-by-date` pass will reopen a closed item, and the failure looks like the feature silently not working.

**`sales_closed` has exactly three readers, all in `Pdc.Ecommerce.ScheduledTasks`.**
The store filters on `item_display` and the point of sale on `item_admin_only`, so no purchasing path reads this column.
Keeping that blast radius small is what makes the column safe to add to live tables.

**Never write `event_item_cutoff_group_item_result` by hand.**
It records the state a cutoff found, and it is the only record of what a cutoff changed.

**The API owns the derived columns.**
`eicg_cutoff_utc` and `eicg_post_send_utc` are computed from the wall-clock time, the IANA zone and the lead.
Editing `eicg_cutoff_local` directly in a database IDE without recomputing `eicg_cutoff_utc` changes what the screen shows and not when the cutoff fires.

## Verification

```bash
~/.claude/skills/mariadb-ddl/scripts/ddl query "SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = 'wcs_event_model_dev' AND COLUMN_NAME LIKE '%sales_closed%' ORDER BY TABLE_NAME"
~/.claude/skills/mariadb-ddl/scripts/ddl query "SHOW CREATE TABLE wcs_event_model_dev.event_item_cutoff_group"
~/.claude/skills/mariadb-ddl/scripts/ddl query "SELECT ROUTINE_NAME FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA = 'wcs_event_model_dev' AND (ROUTINE_NAME LIKE '%item_cutoff%' OR ROUTINE_NAME LIKE '%sales_closed%' OR ROUTINE_NAME LIKE '%admin_only%' OR ROUTINE_NAME = 'select_event_role_recipients_by_event_uuid_and_roles') ORDER BY ROUTINE_NAME"
```

Expect four `*_sales_closed` columns and nineteen new or replaced procedures.

The application database user needs `EXECUTE` on every procedure here, the same grant pattern as the waiver procedures.
