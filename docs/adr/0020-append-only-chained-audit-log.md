# 20. Append-only, hash-chained audit log

- Status: accepted
- Date: 2026-11-14

## Context

GAME_DESIGN §9: every admin action is logged (who, what, when, before/after), can be
read, and cannot be changed. Done criteria of phase 2: every step of a moderator's
work appears in the audit.

## Decision

- **One action, one entry, one transaction.** Stores that make an admin change write
  its entry with `AuditWriter.AppendAsync(tx, …)` in the same transaction: if the
  entry fails, the action rolls back. Standalone events (sign-ins, failures, reading
  the log, verifying it) go through `IAuditLog.AppendAsync`.
- **Content**: time, actor id and the roles held at that moment (or "system"), a
  stable action code (`staff.roles_changed`, `admin.signed_in`…), target type and id,
  `before` and `after` as JSON limited to the fields that changed, reason, actor IP,
  request id. No foreign keys: the log outlives what it describes.
- **Cannot be changed**, in three layers:
  1. a trigger rejects `UPDATE`, `DELETE` and `TRUNCATE` on `audit_log`;
  2. the application has no code path that modifies an entry;
  3. **hash chain**: each entry stores the previous entry's hash and
     `sha256` of a canonical JSON array of all its fields and that hash, computed by
     a SQL function (`audit_digest`) both when writing and when verifying, so that
     `jsonb` normalisation and time zones cannot make them differ. Appends take an
     advisory lock, so the chain stays linear under concurrency.
     `GET /api/v1/admin/audit/verify` recomputes the chain and reports the first entry
     that does not match: even an edit by a database superuser with the trigger
     disabled is detected (tested).
- **Reading**: newest first, cursor pagination, filters by actor, action (or action
  family), target and period. Reading the log is itself recorded (first page only).
- `/dev/reset` empties the game, never the audit log.

## Consequences

- The log only grows; its retention and anonymisation after a data deletion request
  are decided with the GDPR tools (step c): entries keep identifiers, not e-mails.
- Verifying the chain reads the whole table; fine at the expected volume (team
  actions), to be paginated if it ever grows large.
