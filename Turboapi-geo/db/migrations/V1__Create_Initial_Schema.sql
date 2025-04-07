-- Enable PostGIS extension
CREATE EXTENSION IF NOT EXISTS postgis;

-- Create read model table
CREATE TABLE locations_read (
                                id UUID PRIMARY KEY,
                                owner_id VARCHAR(255) NOT NULL,
                                geometry geometry(Point, 4326) NOT NULL,
                                name VARCHAR NOT NULL,
                                description VARCHAR,
                                icon VARCHAR,
                                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                updated_at TIMESTAMP WITH TIME ZONE
);

-- Index for owner lookups
CREATE INDEX idx_locations_read_owner
    ON locations_read(owner_id);

-- Spatial index for geometry queries
CREATE INDEX idx_locations_read_geometry
    ON locations_read USING GIST(geometry);

-- Function to update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
    RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Trigger to automatically update updated_at
CREATE TRIGGER update_locations_read_updated_at
    BEFORE UPDATE ON locations_read
    FOR EACH ROW
EXECUTE FUNCTION update_updated_at_column();

-- Comments for documentation
COMMENT ON TABLE locations_read IS 'Read model for location data with PostGIS support';
COMMENT ON COLUMN locations_read.id IS 'UUID primary key';
COMMENT ON COLUMN locations_read.owner_id IS 'Owner UUID for access control';
COMMENT ON COLUMN locations_read.geometry IS 'PostGIS Point geometry in EPSG:4326';
COMMENT ON COLUMN locations_read.name IS 'Display name of location';
COMMENT ON COLUMN locations_read.description IS 'Description of location';
COMMENT ON COLUMN locations_read.icon IS 'Display icon for the location';
COMMENT ON COLUMN locations_read.created_at IS 'Timestamp of record creation';
COMMENT ON COLUMN locations_read.updated_at IS 'Timestamp of last update';