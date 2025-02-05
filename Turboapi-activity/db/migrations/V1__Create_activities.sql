CREATE TABLE activity_query (
                                position UUID PRIMARY KEY,
                                activity_id UUID NOT NULL,
                                owner_id UUID NOT NULL,
                                name VARCHAR(255) NOT NULL,
                                description TEXT,
                                icon VARCHAR(255)
);

CREATE INDEX idx_activity_query_activity_id ON activity_query(activity_id);
CREATE INDEX idx_activity_query_owner_id ON activity_query(owner_id);