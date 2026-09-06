CREATE TABLE principal (
    id           INTEGER PRIMARY KEY,
    name         TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL DEFAULT '',
    is_enabled   INTEGER NOT NULL DEFAULT 1,
    created_utc  TEXT NOT NULL,
    disabled_utc TEXT
);

CREATE TABLE principal_scope (
    principal_id INTEGER NOT NULL REFERENCES principal(id) ON DELETE CASCADE,
    scope        TEXT NOT NULL,
    PRIMARY KEY (principal_id, scope)
);

CREATE TABLE oauth_identity (
    id           INTEGER PRIMARY KEY,
    principal_id INTEGER NOT NULL REFERENCES principal(id) ON DELETE CASCADE,
    provider     TEXT NOT NULL,
    subject      TEXT NOT NULL,
    email        TEXT,
    linked_utc   TEXT NOT NULL,
    UNIQUE (provider, subject)
);

CREATE TABLE client_certificate (
    id            INTEGER PRIMARY KEY,
    principal_id  INTEGER NOT NULL REFERENCES principal(id) ON DELETE CASCADE,
    thumbprint    TEXT NOT NULL UNIQUE,
    subject       TEXT NOT NULL,
    not_after_utc TEXT NOT NULL,
    revoked_utc   TEXT
);

CREATE TABLE api_token (
    id            INTEGER PRIMARY KEY,
    principal_id  INTEGER NOT NULL REFERENCES principal(id) ON DELETE CASCADE,
    name          TEXT NOT NULL,
    prefix        TEXT NOT NULL UNIQUE,
    secret_hash   BLOB NOT NULL,
    created_utc   TEXT NOT NULL,
    expires_utc   TEXT,
    last_used_utc TEXT,
    revoked_utc   TEXT
);

CREATE TABLE session (
    id               INTEGER PRIMARY KEY,
    principal_id     INTEGER NOT NULL REFERENCES principal(id) ON DELETE CASCADE,
    scheme           TEXT NOT NULL,
    created_utc      TEXT NOT NULL,
    absolute_end_utc TEXT NOT NULL,
    ended_utc        TEXT,
    address          TEXT,
    agent            TEXT
);

CREATE INDEX ix_session_principal ON session(principal_id, ended_utc);

CREATE TABLE refresh_token (
    id            INTEGER PRIMARY KEY,
    session_id    INTEGER NOT NULL REFERENCES session(id) ON DELETE CASCADE,
    token_hash    BLOB NOT NULL UNIQUE,
    issued_utc    TEXT NOT NULL,
    expires_utc   TEXT NOT NULL,
    grace_end_utc TEXT,
    used_utc      TEXT,
    replaced_by   INTEGER REFERENCES refresh_token(id)
);

CREATE INDEX ix_refresh_session ON refresh_token(session_id);

CREATE TABLE audit (
    id           INTEGER PRIMARY KEY,
    at_utc       TEXT NOT NULL,
    principal_id INTEGER REFERENCES principal(id),
    scheme       TEXT,
    action       TEXT NOT NULL,
    target       TEXT,
    detail       TEXT,
    address      TEXT
);

CREATE INDEX ix_audit_at ON audit(at_utc);
