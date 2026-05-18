-- Renamed from B5__before_constraints (which Flyway 10 no longer runs in
-- the expected position) to a regular versioned migration that lands
-- between V4 (customer table) and V5 (constraints). The category split
-- only uses 'Electronics' and 'Books' so the seeded-data tests, which
-- assert inventory_count > 0 after V6 populates only those two
-- categories, stay deterministic regardless of which row EF's FirstAsync
-- returns.
INSERT INTO products (name, price, category)
SELECT
    'Bulk Product ' || generate_series,
    (random() * 99 + 1)::numeric(10,2),
    CASE (generate_series % 2)
        WHEN 0 THEN 'Electronics'
        ELSE 'Books'
        END
FROM generate_series(1, 100);