ALTER TABLE products ADD COLUMN inventory_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE products ADD COLUMN reorder_point INTEGER NOT NULL DEFAULT 10;

UPDATE products SET inventory_count = 100 WHERE category = 'Electronics';
UPDATE products SET inventory_count = 50 WHERE category = 'Books';

CREATE OR REPLACE VIEW inventory_status AS
SELECT
    name,
    category,
    inventory_count,
    reorder_point,
    CASE
        WHEN inventory_count <= reorder_point THEN true
        ELSE false
        END as needs_reorder
FROM products;