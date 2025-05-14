-- Second migration: Add description and icon columns (name already exists)
-- Add only the columns that don't exist yet
DO $$
    BEGIN
        -- Add description column if it doesn't exist
        IF NOT EXISTS (SELECT FROM information_schema.columns
                       WHERE table_name = 'locations_read' AND column_name = 'description') THEN
            ALTER TABLE locations_read ADD COLUMN description TEXT;
        END IF;

        -- Add icon column if it doesn't exist
        IF NOT EXISTS (SELECT FROM information_schema.columns
                       WHERE table_name = 'locations_read' AND column_name = 'icon') THEN
            ALTER TABLE locations_read ADD COLUMN icon VARCHAR(100);
        END IF;
    END
$$;

-- Create index on name column if it doesn't exist
DO $$
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_locations_read_name') THEN
            CREATE INDEX idx_locations_read_name ON locations_read(name);
        END IF;
    END
$$;

-- Update table comments (these will overwrite any existing comments)
COMMENT ON COLUMN locations_read.name IS 'Display name for the location';
COMMENT ON COLUMN locations_read.description IS 'Detailed description of the location';
COMMENT ON COLUMN locations_read.icon IS 'Icon identifier for the location';

-- First drop the index that references this column
DROP INDEX IF EXISTS idx_locations_read_is_deleted;

-- Then drop the column itself
ALTER TABLE locations_read
    DROP COLUMN IF EXISTS is_deleted;

-- Update table comment to reflect changes
COMMENT ON TABLE locations_read IS 'Read model for location data with PostGIS support (without soft delete)';