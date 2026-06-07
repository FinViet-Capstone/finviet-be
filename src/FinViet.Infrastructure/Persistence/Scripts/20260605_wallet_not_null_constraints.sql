DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM wallet
        WHERE customer_id IS NULL
           OR balance IS NULL
    ) THEN
        RAISE EXCEPTION 'Cannot apply NOT NULL wallet constraints while NULL customer_id or balance rows still exist.';
    END IF;
END $$;

ALTER TABLE wallet
    ALTER COLUMN customer_id SET NOT NULL;

ALTER TABLE wallet
    ALTER COLUMN balance SET NOT NULL;
