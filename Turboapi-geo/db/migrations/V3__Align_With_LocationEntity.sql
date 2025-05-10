-- Step 1: Change owner_id from VARCHAR(255) to UUID
ALTER TABLE locations_read
    ADD COLUMN owner_id_uuid UUID;

UPDATE locations_read
SET owner_id_uuid = owner_id::UUID
WHERE owner_id IS NOT NULL;

-- Drop the old owner_id index and column
DROP INDEX IF EXISTS idx_locations_read_owner;
ALTER TABLE locations_read
    DROP COLUMN owner_id;

-- Rename the new UUID column to owner_id
ALTER TABLE locations_read
    RENAME COLUMN owner_id_uuid TO owner_id;

-- Add NOT NULL constraint to the new owner_id column
-- This will fail if any owner_id_uuid became NULL during conversion due to invalid source data.
ALTER TABLE locations_read
    ALTER COLUMN owner_id SET NOT NULL;

-- Recreate the index on the new owner_id UUID column
CREATE INDEX idx_locations_read_owner
    ON locations_read(owner_id);

-- Update comment for owner_id
COMMENT ON COLUMN locations_read.owner_id IS 'Owner identifier (UUID) for access control';


-- Step 2: Ensure 'name' is NOT NULL as per `required string Name` in LocationEntity
-- If there are existing NULL values in 'name', you must handle them first.
-- Example: UPDATE locations_read SET name = 'Unnamed Location' WHERE name IS NULL;
ALTER TABLE locations_read
    ALTER COLUMN name SET NOT NULL;

-- Step 3: Timestamps (CreatedAt and UpdatedAt)
COMMENT ON COLUMN locations_read.created_at IS 'Timestamp of record creation (DB default, EF ValueGeneratedOnAdd)';
COMMENT ON COLUMN locations_read.updated_at IS 'Timestamp of last update (DB trigger, EF ValueGeneratedOnAddOrUpdate)';


-- Final comment update for the table
COMMENT ON TABLE locations_read IS 'Read model for location data. owner_id is UUID. name is NOT NULL. Timestamps are EF Core mapped.';