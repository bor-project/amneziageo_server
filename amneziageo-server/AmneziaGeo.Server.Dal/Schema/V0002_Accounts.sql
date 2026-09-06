ALTER TABLE principal ADD COLUMN kind TEXT NOT NULL DEFAULT 'local';

ALTER TABLE principal ADD COLUMN role TEXT NOT NULL DEFAULT 'none';

CREATE TABLE password_credential (
    principal_id     INTEGER PRIMARY KEY REFERENCES principal(id) ON DELETE CASCADE,
    algorithm        TEXT NOT NULL,
    iterations       INTEGER NOT NULL,
    salt             BLOB NOT NULL,
    hash             BLOB NOT NULL,
    changed_utc      TEXT NOT NULL,
    must_change      INTEGER NOT NULL DEFAULT 0,
    failed_count     INTEGER NOT NULL DEFAULT 0,
    locked_until_utc TEXT
);

CREATE TABLE host_identity (
    principal_id   INTEGER PRIMARY KEY REFERENCES principal(id) ON DELETE CASCADE,
    user_name      TEXT NOT NULL UNIQUE,
    uid            INTEGER NOT NULL,
    registered_utc TEXT NOT NULL,
    seen_utc       TEXT
);
