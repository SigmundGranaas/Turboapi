CREATE OR REPLACE VIEW order_summary AS
SELECT
    o.id as order_id,
    c.name as customer_name,
    p.name as product_name,
    o.quantity,
    p.price * o.quantity as total_price,
    o.order_date,
    o.status
FROM orders o
         JOIN products p ON o.product_id = p.id
         LEFT JOIN customers c ON o.customer_id = c.id;