CREATE TABLE "Accounts" (
                            "Id" UUID PRIMARY KEY,
                            "Email" VARCHAR(320) NOT NULL,
                            "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            "LastLoginAt" TIMESTAMP NULL
);

CREATE UNIQUE INDEX "IX_Accounts_Email" ON "Accounts" ("Email");

CREATE TABLE "AuthenticationMethods" (
                                         "Id" UUID PRIMARY KEY,
                                         "AccountId" UUID NOT NULL,
                                         "Provider" VARCHAR(50) NOT NULL,
                                         "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                         "LastUsedAt" TIMESTAMP NULL,
                                         "AuthType" VARCHAR(20) NOT NULL,
    -- PasswordAuthentication fields
                                         "PasswordHash" VARCHAR(256) NULL,
                                         "Salt" VARCHAR(128) NULL,
    -- OAuthAuthentication fields
                                         "ExternalUserId" VARCHAR(100) NULL,
                                         "AccessToken" VARCHAR(2000) NULL,
                                         "RefreshToken" VARCHAR(2000) NULL,
                                         "TokenExpiry" TIMESTAMP NULL,
    -- WebAuthnAuthentication fields
                                         "CredentialId" VARCHAR(1000) NULL,
                                         "PublicKey" VARCHAR(1000) NULL,
                                         "DeviceName" VARCHAR(100) NULL,
                                         CONSTRAINT "FK_AuthenticationMethods_Accounts"
                                             FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_AuthenticationMethods_AccountId" ON "AuthenticationMethods" ("AccountId");
CREATE UNIQUE INDEX "IX_AuthenticationMethods_Provider_ExternalUserId"
    ON "AuthenticationMethods" ("Provider", "ExternalUserId")
    WHERE "AuthType" = 'OAuth';

CREATE TABLE "UserRoles" (
                             "Id" UUID PRIMARY KEY,
                             "AccountId" UUID NOT NULL,
                             "Role" VARCHAR(50) NOT NULL,
                             "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                             CONSTRAINT "FK_UserRoles_Accounts"
                                 FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_UserRoles_AccountId_Role" ON "UserRoles" ("AccountId", "Role");

CREATE TABLE "RefreshTokens" (
                                 "Id" UUID PRIMARY KEY,
                                 "AccountId" UUID NOT NULL,
                                 "Token" VARCHAR(256) NOT NULL,
                                 "ExpiryTime" TIMESTAMP NOT NULL,
                                 "IsRevoked" BOOLEAN NOT NULL DEFAULT FALSE,
                                 "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                 "RevokedReason" VARCHAR(1000) NULL,
                                 CONSTRAINT "FK_RefreshTokens_Accounts"
                                     FOREIGN KEY ("AccountId") REFERENCES "Accounts" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_RefreshTokens_Token" ON "RefreshTokens" ("Token");
CREATE INDEX "IX_RefreshTokens_AccountId" ON "RefreshTokens" ("AccountId");