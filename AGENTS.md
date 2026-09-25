# AGENTS.md

This file provides guidance to Claude Code (claude.ai/code) and other coding agents when working with code in this repository.
CLAUDE.md is a symbolic link to this file.

## Build & Run

```bash
dotnet build
DOTNET_ROLL_FORWARD=LatestMajor dotnet test                                  # net8.0 tests on a machine with only a newer runtime
dotnet test --filter "ClassName=ItemCutoffExecutorServiceTests"              # single test class
dotnet run --project Pdc.Mobile.ItemCutoffs.ConsoleHost -- --dry-run         # evaluate and log, write nothing
dotnet run --project Pdc.Mobile.ItemCutoffs.ConsoleHost -- --group 12        # one deadline only
dotnet run --project Pdc.Mobile.ItemCutoffs.ConsoleHost                      # one real tick
dotnet publish Pdc.Mobile.ItemCutoffs.Aws.Lambda -c Release -r linux-x64 -o bin/publish
```

## Architecture

**.NET 8 AWS Lambda** invoked every minute by an EventBridge Scheduler schedule (`rate(1 minute)`, a CloudFormation parameter) with `{ messageType: "item.cutoff.tick", groupId?, dryRun? }`.
One invocation is one **tick** (`ItemCutoffExecutorService`): publish any announcement that is due, close any sales deadline that is due, clear the ecommerce cache once if anything changed.
The handler returns the tick summary so a manual `aws lambda invoke` shows its counts; any other `messageType` is logged `##_ITEMCUTOFF_TRIGGER_IGNORED` and skipped.

A sales deadline is created and edited in the mobile app through `Pdc.Mobile`'s `/api/events/item-cutoffs` endpoints.
This function never creates one; it only executes what is already stored.

**The tick, in order:**

1. `IEventItemCutoffGroupRepository.FindDue(now, maxAttempts)` - ONE indexed query across all events, returning nothing on a quiet tick. `groupId` filters the result and never bypasses the due gate.
2. **Announcements first**, so that when a short lead puts both halves on one tick, attendees see the notice before the items vanish. An announcement whose cutoff time has **already passed is skipped**, not posted late: after an outage a backlog would otherwise tell attendees to hurry for a contest that closed an hour ago.
3. **Cutoff.** `ItemCutoffApplier` expands the stored internal codes into every product row sharing them and sets three flags per row: `sales_closed = 1` first (so a crash mid-item leaves it protected from the visibility jobs), then `admin_only = 1`, then `display = 0` last. The prior value of all three is written to `event_item_cutoff_group_item_result` **before** each row is touched. A **late cutoff still runs**: unlike an announcement there is no point after which closing is pointless.
4. **Cache**, once per tick rather than once per deadline, because several deadlines commonly share a closing minute. Ten POSTs 250 ms apart, the copy of `Pdc.Ecommerce.ScheduledTasks`'s `EcommerceCacheService`, because every web server behind the ALB holds its own local cache.

5. **Emails.** The **cutoff-completed** email goes out the moment a deadline closes, telling the registration desk to stop selling and clear the point of sale cache; it is the only thing that makes the point of sale actually stop, so it defaults ON even for an event with no settings row. The **daily digest** reproduces the registration-team workflow email for the event's local day, and is also sent on demand when the API has recorded a request. Neither ever throws: an email reports work that has already happened.

**Digests run on every tick, including one where nothing is due.** The digest goes out in the morning and the contests close in the afternoon, so gating them on due deadlines would mean they almost never sent. That early return was written once and caught by `DigestSchedulingTests`.

**Digest scheduling is decided here, not in SQL,** because MariaDB cannot resolve an IANA zone. `select_event_item_cutoff_settings_due` is a coarse candidate filter; the tick converts to the event's local time and sends when the configured time has passed and today's has not gone. A scheduled send stamps `eics_digest_last_sent_local_date` only on success, so a transient failure retries; an on-demand request is cleared either way, because an admin who sees no email presses the button again.

**Failure isolation:** every deadline is wrapped (`##_ITEMCUTOFF_GROUP_FAILED`), so one event's bad data cannot stop another event's contest closing on time.
A failed half stays `pending` and is retried on later ticks until `cutoff:maxAttempts` (default 3), then becomes `failed` so it stops consuming every tick forever; re-enabling the deadline resets both counters.
The handler throws **only** when a tick cannot start, so the Lambda `Errors` metric means "sales deadlines are not running".

**Solution projects:** core (`Pdc.Mobile.ItemCutoffs`: models, services, repositories, `LogProperties`), `Runtime` (Autofac `CompositionRoot`), `Aws.Lambda` (`Function.cs`, Docker), `ConsoleHost`, `UnitTests` (MSTest + Moq). Same skeleton as `Pdc.Mobile.AttendeeSync`.

**Infrastructure:** `cloudformation.yml` provisions the Lambda (arm64 image, `ReservedConcurrentExecutions: 1` so ticks never overlap and a deadline cannot be closed twice), the Scheduler schedule + invoke role (`FlexibleTimeWindow OFF`, `MaximumRetryAttempts 0`: the next tick a minute later is the retry), and, only when `AlarmNotificationEmail` is set (prod), an SNS topic + `Errors` alarm on a 5-minute period.
Deploy with `--capabilities CAPABILITY_NAMED_IAM`; both parameter files ship `ScheduleState=DISABLED` for the first deployment (dry run, one real run, then enable).
MariaDB DDL + procs in `database/`, run manually per environment - **`Pdc.Ecommerce.ScheduledTasks` must deploy before anything sets `sales_closed`**, or the next daily visibility pass reopens a closed item.

## Configuration

Same encrypted-config pattern as Contests and AttendeeSync: the deployed function reads the **committed KMS ciphertext** at `Pdc.Mobile.ItemCutoffs.Aws.Lambda/Configuration/Target/appsettings.json` (baked into the image; `cloudformation.yml` sets only `APP_CONFIG_ENCRYPTION_KEY` / `APP_CONFIG_ENCRYPTION_KEY_REGION`, and `APP_CONFIG_ENCRYPTED` overrides the file when set).
`Configuration/appsettings.json` is the plaintext shape only.
**The Target file must be created (with `Pdc.Cryptography.Aws.CLI`) before the first image build.**
ConsoleHost uses `appsettings.json` + User Secrets.

Keys: `connectionString`, `firebaseDatabaseUrl`, `firebaseServiceAccount`, `notifications:sqsQueueUrl` (the SAME queue `Pdc.Mobile` uses), `notifications:sqsRegion`, `ecommerce:cacheUri`, `ecommerce:cacheKey`, `cutoff:maxAttempts` (optional, default 3), `smtp:host`, `smtp:username`, `smtp:password`, `email:fromAddress`, `elasticsearch*`.

The SMTP keys are **required**: both emails are how the registration desk learns to stop selling, so a missing value is a misconfiguration rather than a feature that quietly does nothing.

`ecommerce:cacheUri` is optional on purpose: without it the items still close and the store picks the change up on its own schedule, which is a delay rather than a failure.

## Logging

Serilog console (CloudWatch) + Elasticsearch via `FluentBitWrapperFormatter` into the shared `pdc-mobile` data stream (`BatchAction = Create`); `pdc.app = "mobile-item-cutoffs"`.
`LogProperties` `##_ITEMCUTOFF_*` feature keys; `Function.cs` pushes `RunId` (the Lambda request id) and `MessageType` via `LogContext`, and the executor opens a scope with `GroupId` / `EventId` / `EventUuid` around each deadline.

**A tick with nothing due logs at Debug**, not Information: it happens 1,440 times a day and would otherwise bury the ticks that did something.
Every applied cutoff logs one Information line carrying the deadline id, name, product rows closed and item codes; that line is how "what closed at 1:00 PM, and exactly when" is answered.

## Rules that are load bearing

**The post document and the push message are COPIES of `Pdc.Mobile`'s and must match field for field.**
This Lambda is the **second writer** of `events/{uuid}/news_feed/posts/{id}` and the **second producer** of the `news_feed_post` SQS message.
The app reads both without knowing which wrote them, so a drifted field is a post that renders wrongly only when it came from a sales deadline.
`AnnouncementPublisherTests` pins the serialized shape and the path.

**The copy is posted verbatim and is never written here.**
It was generated, previewed and approved when the admin saved the deadline, so a model being unreachable at 12:30 on an event Saturday cannot lose the post.

**A failed push never retries the post.**
The post is already live, so a retry would write a second one. A missing push is a quiet feed entry; a duplicated post is visible to every attendee.

**`role_admin` is excluded from the recipient roles and WDR support is always copied.**
Platform administrators are not registration staff; including that role sent them every closure notice for every event. `EventRoleRecipients.AlwaysIncludedAddress` means there is always at least one recipient, which is why nothing refuses to send when an event has no role holders.

**All email lives here, including the on-demand digest.**
The API records a request on `eics_digest_requested_for_date` rather than sending it, so the registration-team wording has exactly one renderer. Moving any of it into the API would recreate the duplication `EventRoleRecipients` exists to avoid.

**The point of sale does not close by itself.**
`admin_only = 1` hides the item from non-admin staff, but a running WinClient serves from its own in-process cache until its operator clears it.
That is why the completed email exists, and why the registration-team workflow still says "do not stop selling until the text arrives".

## Cross-project contracts

The news feed post node and the notifications queue now have two writers each; `sales_closed` is a contract with `Pdc.Ecommerce.ScheduledTasks` in the `wdr-onboarding` workspace.
See `../.claude/rules/contracts.md` ("Sales deadlines") and `../sales-deadlines-plan/sales-deadlines-plan.md` (architecture + decision log D1-D18).
