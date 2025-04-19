-- Second migration: Add name, description, and icon columns
ALTER TABLE locations_read
    ADD COLUMN name VARCHAR(255),
    ADD COLUMN description TEXT,
    ADD COLUMN icon VARCHAR(100);

-- Add indexes for potential filtering/searching
CREATE INDEX idx_locations_read_name
    ON locations_read(name);

-- Update table comments
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