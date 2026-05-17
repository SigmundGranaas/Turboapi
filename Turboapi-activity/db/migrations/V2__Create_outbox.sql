CREATE SCHEMA IF NOT EXISTS activity;

CREATE TABLE activity.outbox (
    id                UUID PRIMARY KEY,
    aggregate_id      UUID NOT NULL,
    event_type        TEXT NOT NULL,
    source            TEXT NOT NULL,
    data_content_type TEXT NOT NULL DEFAULT 'application/json',
    payload_json      JSONB NOT NULL,
    headers_json      JSONB NOT NULL DEFAULT '{}'::jsonb,
    occurred_at       TIMESTAMPTZ NOT NULL,
    position          BIGSERIAL NOT NULL,
    dispatched_at     TIMESTAMPTZ NULL,
    attempts          INT NOT NULL DEFAULT 0,
    last_error        TEXT NULL
);

CREATE INDEX outbox_undispatched
    ON activity.outbox (dispatched_at, position)
    WHERE dispatched_at IS NULL;

CREATE INDEX outbox_aggregate
    ON activity.outbox (aggregate_id, position);
