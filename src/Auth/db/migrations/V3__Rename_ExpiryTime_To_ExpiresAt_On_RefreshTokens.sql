ALTER TABLE refresh_tokens RENAME COLUMN expiry_time TO expires_at;

-- Add the new revoked_at column to refresh_tokens
ALTER TABLE refresh_tokens ADD COLUMN revoked_at TIMESTAMP NULL;

-- Existing V3 changes for authentication_methods (or move these to V4 if preferred)
ALTER TABLE authentication_methods RENAME COLUMN provider TO provider_name;
ALTER TABLE authentication_methods RENAME COLUMN refresh_token TO oauth_refresh_token;