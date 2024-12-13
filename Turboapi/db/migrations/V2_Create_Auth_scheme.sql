-- V2__Create_Authentication_Schema.sql

-- Create enum for authentication types
CREATE TYPE auth_provider AS ENUM ('Password', 'Google', 'WebAuthn');

-- Create authentication_methods table
CREATE TABLE authentication_methods (
                                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                                        account_id UUID NOT NULL,
                                        provider auth_provider NOT NULL,
                                        auth_type VARCHAR(50) NOT NULL,
                                        created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                        updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                        last_used_at TIMESTAMP WITH TIME ZONE,

    -- Fields for PasswordAuthentication
                                        password_hash TEXT,

    -- Fields for OAuthAuthentication
                                        external_user_id VARCHAR(255),
                                        access_token TEXT,
                                        refresh_token TEXT,
                                        token_expiry TIMESTAMP WITH TIME ZONE,

    -- Fields for WebAuthnAuthentication
                                        credential_id TEXT,
                                        public_key TEXT,
                                        device_name VARCHAR(255),

                                        CONSTRAINT fk_account FOREIGN KEY (account_id) REFERENCES users(id) ON DELETE CASCADE
);

-- Create indexes
CREATE INDEX idx_auth_methods_account_id ON authentication_methods(account_id);
CREATE UNIQUE INDEX idx_auth_methods_provider_external_id ON authentication_methods(provider, external_user_id)
    WHERE provider = 'Google' AND external_user_id IS NOT NULL;

-- Apply the updated_at trigger to authentication_methods
CREATE TRIGGER update_authentication_methods_updated_at
    BEFORE UPDATE ON authentication_methods
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Migrate existing data
INSERT INTO authentication_methods (
    account_id,
    provider,
    auth_type,
    password_hash,
    created_at,
    updated_at
)
SELECT
    id,
    'Password',
    'Password',
    password_hash,
    created_at,
    updated_at
FROM users
WHERE password_hash IS NOT NULL;

INSERT INTO authentication_methods (
    account_id,
    provider,
    auth_type,
    external_user_id,
    created_at,
    updated_at
)
SELECT
    id,
    'Google',
    'OAuth',
    google_id,
    created_at,
    updated_at
FROM users
WHERE google_id IS NOT NULL;

-- Create temporary table for roles
CREATE TABLE user_roles (
                            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                            user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                            role VARCHAR(50) NOT NULL,
                            created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            UNIQUE(user_id, role)
);

-- Migrate existing roles
INSERT INTO user_roles (user_id, role)
SELECT id, unnest(string_to_array(roles, ','))
FROM users;

-- Remove old columns
ALTER TABLE users RENAME TO accounts;
ALTER TABLE accounts
DROP COLUMN password_hash,
    DROP COLUMN google_id,
    DROP COLUMN roles,
    DROP COLUMN refresh_token,
    DROP COLUMN refresh_token_expiry_time;

-- Rename indexes to match new table name
ALTER INDEX idx_users_email RENAME TO idx_accounts_email;
ALTER INDEX idx_users_google_id RENAME TO idx_accounts_google_id;

-- Update trigger name to match new table name
ALTER TRIGGER update_users_updated_at
    ON accounts RENAME TO update_accounts_updated_at;

-- V3__Add_Email_Verification.sql
ALTER TABLE accounts
    ADD COLUMN is_email_verified BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN email_verification_token TEXT;

-- Set existing accounts as verified since they were working before
UPDATE accounts SET is_email_verified = true;