ALTER TABLE orders ADD CONSTRAINT quantity_positive CHECK (quantity > 0);
ALTER TABLE products ADD CONSTRAINT price_positive CHECK (price > 0);
