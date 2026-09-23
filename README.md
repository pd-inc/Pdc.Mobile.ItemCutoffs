# Pdc.Mobile.ItemCutoffs

Scheduled AWS Lambda that executes **sales deadlines**: it closes a group of event items for sale at a configured time, and optionally announces the deadline in the WDR mobile app news feed a configurable lead beforehand.
Deadlines are created and edited by event admins in the mobile app through `Pdc.Mobile`'s `/api/events/item-cutoffs` endpoints.
This function never creates one; it only executes what is already stored.

Architecture record and decision log: `../sales-deadlines-plan/sales-deadlines-plan.md`.
Contracts: `../.claude/rules/contracts.md` ("Sales deadlines").

---

## The tick

EventBridge Scheduler invokes the Lambda every minute with `{"messageType": "item.cutoff.tick"}`.
One tick:

1. **Find what is due**: one indexed query across all events (`IEventItemCutoffGroupRepository.FindDue`). A quiet tick returns nothing and logs at Debug.
2. **Announcements first**: write the news feed post, then enqueue its push on the notifications queue. An announcement whose cutoff time has already passed is **skipped**, never posted late.
3. **Cutoffs**: expand each deadline's internal codes into every product row sharing them and set `sales_closed = 1`, `admin_only = 1`, then `display = 0`, recording the prior values first. A late cutoff still runs.
4. **Cache**: clear the ecommerce cache once per tick if anything closed.

Every deadline is isolated, so one event's bad data cannot stop another event's contest closing on time.
A failed half is retried on later ticks until `cutoff:maxAttempts` (default 3), then marked `failed`.
The handler throws only when a tick cannot start, so the Lambda `Errors` metric means "sales deadlines are not running".

The point of sale does not close by itself: a running WinClient serves items from its own cache until its operator clears it.

---

## Project structure

| Path | Description |
|---|---|
| `cloudformation.yml` | AWS CloudFormation template (Lambda, EventBridge Scheduler schedule and invoke role, optional errors alarm) |
| `cloudformation-parameters.dev.json` | CloudFormation parameter values for **dev** |
| `cloudformation-parameters.prod.json` | CloudFormation parameter values for **prod** |
| `buildspec.yml` | AWS CodeBuild pipeline definition (publish, Docker build, push to ECR) |
| `database/` | Tables, view change, and stored procedures, run manually per environment (see `database/README.md`) |
| `Pdc.Mobile.ItemCutoffs.Aws.Lambda/` | Lambda entry point, Dockerfile, and configuration |
| `Pdc.Mobile.ItemCutoffs.Runtime/` | Composition root and DI wiring |
| `Pdc.Mobile.ItemCutoffs.ConsoleHost/` | Local runner (`--dry-run`, `--group <id>`) |
| `Pdc.Mobile.ItemCutoffs/` | Core domain logic, services, repositories |
| `Pdc.Mobile.ItemCutoffs.UnitTests/` | MSTest + Moq tests |

---

## Prerequisites

The deploy order across repos is load bearing:

1. **`database/` scripts** applied to the target environment's MariaDB, in numeric order (see `database/README.md`, especially the verification step after script 002).
2. **`Pdc.EventPro` 2.6.0** on the `pd-inc` feed.
3. **`Pdc.Ecommerce.ScheduledTasks`** (in `wdr-onboarding`) deployed with its `sales_closed` guard. Reversed, the next daily item visibility pass reopens a closed item.
4. **`Pdc.Mobile`** deployed, so admins can create deadlines.
5. **This stack**, deployed with the schedule disabled (see below).
6. **The app**.

You also need:

- [AWS CLI v2](https://docs.aws.amazon.com/cli/latest/userguide/install-cliv2.html) installed and configured.
- IAM permissions for CloudFormation, Lambda, IAM, EventBridge Scheduler, SNS, CloudWatch, ECR, and KMS.
- The encrypted configuration committed at `Pdc.Mobile.ItemCutoffs.Aws.Lambda/Configuration/Target/appsettings.json` (see [Configuration](#configuration)).
- An image in the `wdr-mobile-item-cutoffs-lambda` ECR repository, and its tag in the parameter file's `ImageUri` (see [Building the image](#building-the-image)).

---

## Local development

```bash
dotnet build
DOTNET_ROLL_FORWARD=LatestMajor dotnet test                                  # net8.0 tests on a machine with only a newer runtime
dotnet run --project Pdc.Mobile.ItemCutoffs.ConsoleHost -- --dry-run         # evaluate and log, write nothing
dotnet run --project Pdc.Mobile.ItemCutoffs.ConsoleHost -- --group 12        # one deadline only
dotnet run --project Pdc.Mobile.ItemCutoffs.ConsoleHost                      # one real tick
```

The console host uses `appsettings.json` plus .NET User Secrets (`UserSecretsId` `pdc-mobile-item-cutoffs`), with the same keys as the deployed configuration below.

---

## Configuration

Two files under `Pdc.Mobile.ItemCutoffs.Aws.Lambda/Configuration/`, the same arrangement as the Contests, Waiver, and AttendeeSync Lambdas:

| File | Holds | Read by |
|---|---|---|
| `appsettings.json` | The plaintext **shape** only, with every secret blank | Nobody at runtime; it documents what to encrypt |
| `Target/appsettings.json` | The **KMS ciphertext** of the filled-in document, produced by `Pdc.Cryptography.Aws.CLI` (`wdr-shared/Pdc.Cryptography`) with the `pdc-general-crypto` key | The deployed function, at cold start |

`cloudformation.yml` sets only `APP_CONFIG_ENCRYPTION_KEY` and `APP_CONFIG_ENCRYPTION_KEY_REGION`, so the function reads `Configuration/Target/appsettings.json` baked into the image.
Setting `APP_CONFIG_ENCRYPTED` on the function overrides the file.
Changing the configuration therefore means re-encrypting `Target/appsettings.json`, committing it, and building a new image.

| Key | Notes |
|---|---|
| `connectionString` | MariaDB connection string, the same value the Contests and Waiver configs carry |
| `firebaseDatabaseUrl`, `firebaseServiceAccount` | Firebase RTDB, the same values the other mobile Lambdas carry |
| `notifications:sqsQueueUrl` | The **same queue `Pdc.Mobile` uses** for pushes. Prod: `https://sqs.us-west-2.amazonaws.com/376503697510/pdc-mobile-notifications-prod` |
| `notifications:sqsRegion` | `us-west-2` |
| `ecommerce:cacheUri`, `ecommerce:cacheKey` | Optional. Without them items still close, and the store picks the change up on its own schedule |
| `cutoff:maxAttempts` | Optional, default 3 |
| `elasticsearch*` | Log sink, as in the sibling Lambdas |

There is **no dev notifications queue** (only `pdc-mobile-notifications-prod` exists).
Never point a dev configuration at the prod queue: it would send real pushes to prod attendees.

The Lambda runs as the shared `wdr-lambda-role-generic` role, which already grants `sqs:SendMessage` (through `wdr-lambda-policy`) and KMS decrypt.

**Keep every AWS SDK package on v3.** `Pdc.Cryptography.Aws` 1.1.0 decrypts the configuration with the v3 KMS client, so `AWSSDK.SQS` is pinned to 3.7.x like the Contests Lambda. A v4 AWS package pulls in the v4 `AWSSDK.Core` underneath that client, and a failure there is a failure at cold start.

---

## Building the image

CodeBuild project **`wdr-prod-mobile-item-cutoffs-lambda`** runs `buildspec.yml`: `dotnet publish`, `docker build` from `Pdc.Mobile.ItemCutoffs.Aws.Lambda/Dockerfile`, then a push to the `wdr-mobile-item-cutoffs-lambda` ECR repository.
The image tag is `IMAGE_TAG_PREFIX.YYYY-MM-DD.BUILD_NUMBER`, for example `REL.2026-09-23.2`.

Find the latest tag, and the commit it was built from:

```bash
aws ecr describe-images --repository-name wdr-mobile-item-cutoffs-lambda --region us-west-2 \
  --query 'sort_by(imageDetails,&imagePushedAt)[-1].[imageTags[0],imagePushedAt]' --output text

aws codebuild batch-get-builds --region us-west-2 --query 'builds[0].[buildNumber,buildStatus,resolvedSourceVersion]' --output text \
  --ids "$(aws codebuild list-builds-for-project --project-name wdr-prod-mobile-item-cutoffs-lambda --region us-west-2 --query 'ids[0]' --output text)"
```

Put the tag in `ImageUri` in the parameter file before creating or updating the stack.

---

## Deploying the CloudFormation stack

The stack is named `pdc-mobile-item-cutoffs-{env}` and every command below runs from this folder.
The template creates a named IAM role, so every create and update needs `--capabilities CAPABILITY_NAMED_IAM`.

### First deployment (prod)

Both parameter files ship `ScheduleState=DISABLED`, so the first deployment creates the function without starting the minute tick.

1. **Create the stack** with the schedule disabled.

   ```bash
   aws cloudformation create-stack \
     --stack-name pdc-mobile-item-cutoffs-prod \
     --template-body file://cloudformation.yml \
     --parameters file://cloudformation-parameters.prod.json \
     --capabilities CAPABILITY_NAMED_IAM \
     --region us-west-2

   aws cloudformation wait stack-create-complete --stack-name pdc-mobile-item-cutoffs-prod --region us-west-2
   ```

2. **Confirm the alarm subscription.** Because the prod file sets `AlarmNotificationEmail`, SNS emails that address a confirmation link. The alarm notifies nobody until it is clicked.

3. **Dry run.** This evaluates and logs everything and writes nothing. It proves the image starts, the configuration decrypts, and the database is reachable.

   ```bash
   aws lambda invoke --function-name pdc-mobile-item-cutoffs-prod --region us-west-2 \
     --cli-binary-format raw-in-base64-out \
     --payload '{"messageType":"item.cutoff.tick","dryRun":true}' /dev/stdout
   ```

   The response is the tick summary. A `FunctionError` in the output, or an error in the function's CloudWatch logs, means stop here.

4. **One real tick**, the same payload without `dryRun`. With no deadline due it does nothing, which is the expected result.

5. **Enable the schedule.** Change `ScheduleState` to `ENABLED` in `cloudformation-parameters.prod.json`, commit that change, and update the stack.

   ```bash
   aws cloudformation update-stack \
     --stack-name pdc-mobile-item-cutoffs-prod \
     --template-body file://cloudformation.yml \
     --parameters file://cloudformation-parameters.prod.json \
     --capabilities CAPABILITY_NAMED_IAM \
     --region us-west-2

   aws cloudformation wait stack-update-complete --stack-name pdc-mobile-item-cutoffs-prod --region us-west-2
   ```

   Commit the file change rather than overriding the value on the command line. Otherwise the next update from the committed file quietly disables the schedule again.

### Releasing a new image

Set `ImageUri` in the parameter file to the new tag, commit it, and run the `update-stack` and `wait stack-update-complete` commands above.

### Pausing sales deadlines

Set `ScheduleState` to `DISABLED` and update the stack.
Nothing is lost: deadlines that come due while the schedule is off are picked up by the first tick after it is re-enabled.
Cutoffs run late, and announcements whose cutoff has already passed are skipped.
To stop a single deadline instead, disable it in the app.

### Dev

The dev stack is `pdc-mobile-item-cutoffs-dev`, created and updated with the same commands and `cloudformation-parameters.dev.json` (no alarm is created, since `AlarmNotificationEmail` is empty).
The configuration is baked into the image, so a dev stack built from the prod image reads the **prod** configuration.
Only enable a dev schedule on an image built with a dev `Target/appsettings.json`, or two functions would tick against the same database and could close and announce the same deadline twice.

### Other stack commands

```bash
# outputs (function ARN, schedule name)
aws cloudformation describe-stacks --stack-name pdc-mobile-item-cutoffs-prod --region us-west-2 --query "Stacks[0].Outputs"

# why an operation failed
aws cloudformation describe-stack-events --stack-name pdc-mobile-item-cutoffs-prod --region us-west-2 \
  --query "StackEvents[?contains(ResourceStatus,'FAILED')].[LogicalResourceId,ResourceStatusReason]" --output text

# delete
aws cloudformation delete-stack --stack-name pdc-mobile-item-cutoffs-prod --region us-west-2
```

A failed `create-stack` rolls back to `ROLLBACK_COMPLETE`, which cannot be updated: delete the stack and create it again.

### Parameters

| Parameter | Default | Notes |
|---|---|---|
| `Environment` | none | `dev` or `prod`; suffixes every resource name |
| `ImageUri` | none | Full ECR image URI with tag |
| `AppConfigEncryptionKey` | none | `alias/pdc-general-crypto` |
| `AppConfigEncryptionKeyRegion` | none | `us-west-2` |
| `LambdaRoleArn` | `wdr-lambda-role-generic` | Needs KMS decrypt for the config key, `sqs:SendMessage` on the notifications queue, and CloudWatch Logs |
| `TickScheduleExpression` | `rate(1 minute)` | One minute is the precision a deadline is configured at |
| `ScheduleState` | `ENABLED` | Both parameter files ship `DISABLED` for the first deployment |
| `AlarmNotificationEmail` | empty | Set (prod) to create the SNS topic, subscription, and `Errors` alarm on a 5-minute period; empty (dev) creates none |

### What the stack creates

| Resource | Name | Notes |
|---|---|---|
| Lambda function | `pdc-mobile-item-cutoffs-{env}` | arm64 image, 256 MB, 300 s timeout, **reserved concurrency 1** so ticks never overlap and a deadline cannot be closed twice |
| Scheduler invoke role | `pdc-mobile-item-cutoffs-schedule-{env}` | Allowed to invoke only this function |
| Schedule | `pdc-mobile-item-cutoffs-{env}` | UTC, no flexible window, **no retries**: the next tick a minute later is the retry |
| SNS topic, subscription, `Errors` alarm | `pdc-mobile-item-cutoffs-alarms-{env}`, `pdc-mobile-item-cutoffs-errors-{env}` | Only when `AlarmNotificationEmail` is set |

---

## Manual invocation

The schedule's payload is `{"messageType": "item.cutoff.tick"}`.
Two optional fields exist for operators:

```bash
# dry run: evaluate and log everything, write nothing
aws lambda invoke --function-name pdc-mobile-item-cutoffs-prod --region us-west-2 \
  --cli-binary-format raw-in-base64-out \
  --payload '{"messageType":"item.cutoff.tick","dryRun":true}' /dev/stdout

# one deadline only (it must still be enabled and due)
aws lambda invoke --function-name pdc-mobile-item-cutoffs-prod --region us-west-2 \
  --cli-binary-format raw-in-base64-out \
  --payload '{"messageType":"item.cutoff.tick","groupId":12}' /dev/stdout
```

`groupId` narrows a tick and never bypasses the due check.
A payload with any other `messageType` is logged as `##_ITEMCUTOFF_TRIGGER_IGNORED` and does nothing.

---

## Logging

Serilog writes compact JSON to CloudWatch and to Elasticsearch in the shared `pdc-mobile` data stream, with `pdc.app = "mobile-item-cutoffs"`.
Every applied cutoff logs one Information line carrying the deadline id, name, product rows closed, and item codes; that line answers "what closed, and exactly when".
See `Pdc.Mobile.ItemCutoffs.Aws.Lambda/infrastructure/elastic/`.
