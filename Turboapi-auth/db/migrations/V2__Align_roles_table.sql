-- Rename the existing user_roles table to roles
ALTER TABLE user_roles RENAME TO roles;

-- Rename the 'role' column (which stores the role name) to 'name' to match the domain model
ALTER TABLE roles RENAME COLUMN role TO name;

-- If the primary key or any indexes on user_roles were explicitly named (e.g., pk_user_roles),
-- you might want to rename them as well for consistency, though PostgreSQL often handles this.
-- Example: ALTER INDEX IF EXISTS pk_user_roles RENAME TO pk_roles;
-- Example: ALTER INDEX IF EXISTS ix_user_roles_account_id_role RENAME TO ix_roles_account_id_name;

-- Verify the unique index from RoleConfiguration: builder.HasIndex("account_id", nameof(Role.Name)).IsUnique();
-- If an equivalent index already exists on (account_id, name) (formerly account_id, role),
-- PostgreSQL might handle the column rename within the index automatically.
-- If not, or if you want to be explicit:
-- First, drop the old index if it exists and its name didn't change automatically (unlikely for column rename within index)
-- DROP INDEX IF EXISTS ix_user_roles_account_id_role; -- (or whatever its old name was)
-- Then create the new one as per EF Core configuration (if it wasn't implicitly handled by the rename)
-- This step is often not needed if the index definition itself didn't change other than the column name.
-- However, if you had an index just on 'role' and now need one on 'name', you'd add it.
-- Given the EF Core config: HasIndex("account_id", nameof(Role.Name)).IsUnique();
-- Let's ensure this index exists. If your V1 created ix_user_roles_account_id_role,
-- it might have been renamed to ix_user_roles_account_id_name after the column rename.
-- To be safe, let's try to rename it if it exists with the old table prefix, or create it if it doesn't match.

DO $$
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM pg_indexes
            WHERE schemaname = 'public' AND indexname = 'ix_user_roles_account_id_role' -- Old name based on V1
        ) THEN
            ALTER INDEX ix_user_roles_account_id_role RENAME TO ix_roles_account_id_name;
        ELSIF NOT EXISTS (
            SELECT 1
            FROM pg_indexes
            WHERE schemaname = 'public' AND indexname = 'ix_roles_account_id_name' -- New name
        ) THEN
            -- Create the index if it doesn't exist by either old or new name logic path
            -- This assumes the columns account_id and name exist on the roles table
            CREATE UNIQUE INDEX ix_roles_account_id_name ON roles (account_id, name);
        END IF;
    END $$;