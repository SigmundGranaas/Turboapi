CREATE TYPE order_status AS ENUM ('pending', 'completed', 'cancelled');
ALTER TABLE orders ADD COLUMN status order_status NOT NULL DEFAULT 'pending';
