CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE TABLE accounts (
                          id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                          email VARCHAR(320) NOT NULL,
                          created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                          last_login_at TIMESTAMP NULL
);

CREATE UNIQUE INDEX ix_accounts_email ON accounts (email);

CREATE TABLE authentication_methods (
                                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                                        account_id UUID NOT NULL,
                                        provider VARCHAR(50) NOT NULL,
                                        created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                        last_used_at TIMESTAMP NULL,
                                        auth_type VARCHAR(20) NOT NULL,
    -- PasswordAuthentication fields
                                        password_hash VARCHAR(256) NULL,
    -- OAuthAuthentication fields
                                        external_user_id VARCHAR(100) NULL,
                                        access_token VARCHAR(2000) NULL,
                                        refresh_token VARCHAR(2000) NULL,
                                        token_expiry TIMESTAMP NULL,
    -- WebAuthnAuthentication fields
                                        credential_id VARCHAR(1000) NULL,
                                        public_key VARCHAR(1000) NULL,
                                        device_name VARCHAR(100) NULL,
                                        CONSTRAINT fk_authentication_methods_accounts
                                            FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE CASCADE
);

CREATE INDEX ix_authentication_methods_account_id ON authentication_methods (account_id);
CREATE UNIQUE INDEX ix_authentication_methods_provider_external_user_id
    ON authentication_methods (provider, external_user_id)
    WHERE auth_type = 'OAuth';

CREATE TABLE user_roles (
                            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                            account_id UUID NOT NULL,
                            role VARCHAR(50) NOT NULL,
                            created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            CONSTRAINT fk_user_roles_accounts
                                FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ix_user_roles_account_id_role ON user_roles (account_id, role);

CREATE TABLE refresh_tokens (
                                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                                account_id UUID NOT NULL,
                                token VARCHAR(256) NOT NULL,
                                expiry_time TIMESTAMP NOT NULL,
                                is_revoked BOOLEAN NOT NULL DEFAULT FALSE,
                                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                revoked_reason VARCHAR(1000) NULL,
                                CONSTRAINT fk_refresh_tokens_accounts
                                    FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ix_refresh_tokens_token ON refresh_tokens (token);
CREATE INDEX ix_refresh_tokens_account_id ON refresh_tokens (account_id);