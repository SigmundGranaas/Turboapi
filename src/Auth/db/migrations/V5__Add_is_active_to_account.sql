ALTER TABLE accounts
    ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT TRUE;

COMMENT ON COLUMN accounts.is_active IS 'Indicates if the account is active and allowed to log in.';