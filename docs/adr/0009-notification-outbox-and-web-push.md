# 9. Notification outbox and Web Push

- Status: accepted
- Date: 2026-11-14

## Context

Players are notified when a friend falls, when someone saved them, when they fell,
before a storm, and when friends are still below the threshold (GAME_DESIGN §4).
Most of these are decided at midnight, inside the resolution transaction. A push
must never be sent for a night that was rolled back, nor twice, nor days late.
Push is the main reason to come back; spam is the main reason to uninstall.

## Decision

- **Transactional outbox.** Notifications are rows of `notification_outbox`,
  written in the same transaction as what they announce, with a unique
  `dedupe_key` (`kind:day:player`). Midnight and the scheduled jobs can be re-run
  without duplicates.
- **Dispatcher** (background worker, every 2 s): claims a batch with a lease
  (`locked_until`, `FOR UPDATE SKIP LOCKED`, so instances share the work),
  checks preferences, renders the text in the player's language, sends to every
  subscribed device, forgets subscriptions the push service reports as gone
  (404/410), and marks the row sent. If no device could be reached, the row is
  retried after its lease (5 attempts). Rows older than 12 hours expire unsent.
- **What is sent** (each can be turned off in the settings):
  - you fell (surviving is the normal case and sends nothing);
  - friends who fell (one message per night, listing them);
  - who saved you (one message, listing them);
  - a storm, 6 hours before midnight (the storm itself is visible all day);
  - friends still below the threshold: **one** reminder a day, at 21:00 UTC,
    rather than a push each time a friend is below (everyone starts the day at 0);
  - new clothing unlocked.
- **Texts** come from `.resx` resources (French neutral, English satellite), with
  "name" / "name and N others" forms. Messages carry a tag so that a newer message
  replaces an older one on the device.
- **Web Push with VAPID** (`Lib.Net.Http.WebPush`). Production configures the key
  pair; development generates one on first use and keeps it in `vapid_keys`.

## Consequences

- Sending is decoupled from midnight: a slow push service never delays a night.
- At most a handful of pushes per player per day, each one meaningful.
- iOS requires the PWA to be installed for push; the onboarding asks for it (step f).
