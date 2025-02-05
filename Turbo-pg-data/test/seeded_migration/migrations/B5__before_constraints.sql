INSERT INTO products (name, price, category)
SELECT
    'Bulk Product ' || generate_series,
    (random() * 100)::numeric(10,2),
    CASE (random() * 2)::integer
        WHEN 0 THEN 'Electronics'
        WHEN 1 THEN 'Books'
        ELSE 'Other'
        END
FROM generate_series(1, 100);