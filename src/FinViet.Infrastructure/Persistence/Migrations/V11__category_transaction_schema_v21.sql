DO $$
BEGIN
    IF to_regclass('public.categories') IS NOT NULL AND to_regclass('public.category') IS NULL THEN
        RETURN;
    END IF;

    IF to_regclass('public.transaction') IS NOT NULL THEN
        DELETE FROM transaction;
    END IF;

    IF to_regclass('public.category_budget') IS NOT NULL THEN
        DELETE FROM category_budget;
        ALTER TABLE category_budget DROP CONSTRAINT IF EXISTS category_budget_category_id_fkey;
    END IF;

    IF to_regclass('public.category_correction_log') IS NOT NULL THEN
        DELETE FROM category_correction_log;
        ALTER TABLE category_correction_log DROP CONSTRAINT IF EXISTS category_correction_log_corrected_category_id_fkey;
        ALTER TABLE category_correction_log DROP CONSTRAINT IF EXISTS category_correction_log_transaction_id_fkey;
    END IF;

    IF to_regclass('public.beneficiary_rule') IS NOT NULL THEN
        DELETE FROM beneficiary_rule;
        ALTER TABLE beneficiary_rule DROP CONSTRAINT IF EXISTS beneficiary_rule_category_id_fkey;
    END IF;

    IF to_regclass('public.category_request') IS NOT NULL THEN
        UPDATE category_request SET created_category_id = NULL;
        ALTER TABLE category_request DROP CONSTRAINT IF EXISTS category_request_created_category_id_fkey;
    END IF;

    IF to_regclass('public.user_category_buckets') IS NOT NULL THEN
        DELETE FROM user_category_buckets;
        ALTER TABLE user_category_buckets DROP CONSTRAINT IF EXISTS user_category_buckets_category_id_fkey;
    END IF;

    IF to_regclass('public.transaction') IS NOT NULL THEN
        ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_category_id_fkey;
        ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_ai_category_guess_fkey;
        ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_wallet_id_fkey;
        ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_batch_id_fkey;
        ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_report_id_fkey;
        ALTER TABLE transaction DROP CONSTRAINT IF EXISTS transaction_source_id_fkey;
    END IF;

    IF to_regclass('public.category') IS NOT NULL THEN
        DELETE FROM category;
        ALTER TABLE category DROP CONSTRAINT IF EXISTS category_pkey;
        ALTER TABLE category ALTER COLUMN category_id DROP DEFAULT;
        ALTER TABLE category ALTER COLUMN category_id TYPE varchar(40) USING category_id::text;
        ALTER TABLE category RENAME COLUMN category_id TO id;
        IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'category' AND column_name = 'category_name'
        ) AND NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'category' AND column_name = 'name_vi'
        ) THEN
            ALTER TABLE category RENAME COLUMN category_name TO name_vi;
        END IF;
        ALTER TABLE category DROP COLUMN IF EXISTS category_name;
        ALTER TABLE category ADD COLUMN IF NOT EXISTS name_en varchar(80);
        ALTER TABLE category ADD COLUMN IF NOT EXISTS icon varchar(60);
        ALTER TABLE category ADD COLUMN IF NOT EXISTS color varchar(7);
        ALTER TABLE category ADD COLUMN IF NOT EXISTS sort_order integer;
        ALTER TABLE category ADD COLUMN IF NOT EXISTS default_bucket varchar(20);
        UPDATE category SET default_bucket = lower(expense_class) WHERE default_bucket IS NULL AND expense_class IS NOT NULL;
        ALTER TABLE category DROP COLUMN IF EXISTS expense_class;
        ALTER TABLE category ADD CONSTRAINT categories_pkey PRIMARY KEY (id);
        ALTER TABLE category RENAME TO categories;
    END IF;

    IF to_regclass('public.category_budget') IS NOT NULL THEN
        ALTER TABLE category_budget ALTER COLUMN category_id TYPE varchar(40) USING category_id::text;
        ALTER TABLE category_budget ADD CONSTRAINT category_budget_category_id_fkey FOREIGN KEY (category_id) REFERENCES categories(id) ON DELETE CASCADE;
    END IF;

    IF to_regclass('public.category_correction_log') IS NOT NULL THEN
        ALTER TABLE category_correction_log ALTER COLUMN corrected_category_id TYPE varchar(40) USING corrected_category_id::text;
        ALTER TABLE category_correction_log ALTER COLUMN original_ai_guess TYPE varchar(40) USING original_ai_guess::text;
        ALTER TABLE category_correction_log ADD CONSTRAINT category_correction_log_corrected_category_id_fkey FOREIGN KEY (corrected_category_id) REFERENCES categories(id) ON DELETE CASCADE;
    END IF;

    IF to_regclass('public.beneficiary_rule') IS NOT NULL THEN
        ALTER TABLE beneficiary_rule ALTER COLUMN category_id TYPE varchar(40) USING category_id::text;
        ALTER TABLE beneficiary_rule ADD CONSTRAINT beneficiary_rule_category_id_fkey FOREIGN KEY (category_id) REFERENCES categories(id) ON DELETE CASCADE;
    END IF;

    IF to_regclass('public.category_request') IS NOT NULL THEN
        ALTER TABLE category_request ALTER COLUMN created_category_id TYPE varchar(40) USING created_category_id::text;
        ALTER TABLE category_request ADD CONSTRAINT category_request_created_category_id_fkey FOREIGN KEY (created_category_id) REFERENCES categories(id) ON DELETE SET NULL;
    END IF;

    IF to_regclass('public.user_category_buckets') IS NOT NULL THEN
        ALTER TABLE user_category_buckets ALTER COLUMN category_id TYPE varchar(40) USING category_id::text;
        ALTER TABLE user_category_buckets ADD CONSTRAINT user_category_buckets_category_id_fkey FOREIGN KEY (category_id) REFERENCES categories(id) ON DELETE CASCADE;
    END IF;

    IF to_regclass('public.transaction') IS NOT NULL THEN
        ALTER TABLE transaction RENAME COLUMN transaction_id TO id;
        ALTER TABLE transaction RENAME COLUMN transaction_type TO type;
        ALTER TABLE transaction RENAME COLUMN note TO description;
        ALTER TABLE transaction RENAME COLUMN beneficiary_name TO merchant;
        ALTER TABLE transaction RENAME COLUMN source_channel TO entry_method;
        ALTER TABLE transaction ALTER COLUMN category_id TYPE varchar(40) USING category_id::text;
        ALTER TABLE transaction ADD COLUMN IF NOT EXISTS customer_id uuid;
        ALTER TABLE transaction ADD COLUMN IF NOT EXISTS transfer_pair_id uuid;
        ALTER TABLE transaction ADD COLUMN IF NOT EXISTS external_id varchar(120);
        ALTER TABLE transaction ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
        ALTER TABLE transaction ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();
        UPDATE transaction t SET customer_id = w.customer_id FROM wallet w WHERE t.wallet_id = w.wallet_id AND t.customer_id IS NULL;
        UPDATE transaction SET type = lower(type);
        UPDATE transaction SET entry_method = CASE upper(entry_method)
            WHEN 'MANUAL' THEN 'manual'
            WHEN 'SMS' THEN 'sms_paste'
            WHEN 'CSV' THEN 'csv_import'
            WHEN 'LINKED' THEN 'sepay_sync'
            ELSE lower(entry_method)
        END;
        ALTER TABLE transaction ALTER COLUMN customer_id SET NOT NULL;
        ALTER TABLE transaction ALTER COLUMN entry_method SET NOT NULL;
        ALTER TABLE transaction DROP COLUMN IF EXISTS source_id;
        ALTER TABLE transaction DROP COLUMN IF EXISTS batch_id;
        ALTER TABLE transaction DROP COLUMN IF EXISTS report_id;
        ALTER TABLE transaction DROP COLUMN IF EXISTS is_ai_classified;
        ALTER TABLE transaction DROP COLUMN IF EXISTS ai_confidence;
        ALTER TABLE transaction DROP COLUMN IF EXISTS ai_category_guess;
        ALTER TABLE transaction ADD CONSTRAINT transactions_category_id_fkey FOREIGN KEY (category_id) REFERENCES categories(id) ON DELETE SET NULL;
        ALTER TABLE transaction ADD CONSTRAINT transactions_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES customer(customer_id) ON DELETE CASCADE;
        ALTER TABLE transaction ADD CONSTRAINT transactions_wallet_id_fkey FOREIGN KEY (wallet_id) REFERENCES wallet(wallet_id) ON DELETE CASCADE;
        ALTER TABLE transaction RENAME TO transactions;
    END IF;
END $$;

INSERT INTO categories (id, name_vi, name_en, type, icon, color, default_bucket, sort_order, is_mandatory)
VALUES
('cat_food','Ăn uống','Food','expense','restaurant','#4EDEA3','needs',1,true),
('cat_housing','Nhà ở & Tiện ích','Housing & Utilities','expense','home','#D0BCFF','needs',2,true),
('cat_transport','Di chuyển','Transport','expense','directions_car','#90CAF9','needs',3,true),
('cat_health','Sức khỏe & Y tế','Health','expense','local_hospital','#EF9A9A','needs',4,true),
('cat_education','Giáo dục','Education','expense','school','#FFE082','needs',5,true),
('cat_family','Gửi tiền gia đình','Family support','expense','family_restroom','#BCAAA4','needs',6,true),
('cat_entertain','Giải trí','Entertainment','expense','sports_esports','#CE93D8','wants',7,true),
('cat_beauty','Quần áo & Thời trang','Fashion','expense','checkroom','#F8BBD0','wants',8,true),
('cat_shopping','Mua sắm online','Shopping','expense','shopping_bag','#FFB690','wants',9,true),
('cat_dining','Ăn ngoài & Cà phê','Dining & Coffee','expense','local_cafe','#FFCC80','wants',10,true),
('cat_savings','Tiết kiệm','Savings','expense','savings','#4EDEA3','savings',11,true),
('cat_invest','Đầu tư','Investment','expense','trending_up','#A5D6A7','savings',12,true),
('cat_savings_goal','Mục tiêu tiết kiệm','Saving goal','expense','flag','#80CBC4','savings',13,true),
('cat_salary','Lương','Salary','income','payments','#81C784',NULL,101,true),
('cat_freelance','Freelance','Freelance','income','work','#64B5F6',NULL,102,true),
('cat_investment_return','Lợi nhuận đầu tư','Investment return','income','trending_up','#A5D6A7',NULL,103,true),
('cat_gift','Quà tặng','Gift','income','redeem','#F48FB1',NULL,104,true),
('cat_income_other','Thu nhập khác','Other income','income','more_horiz','#B0BEC5',NULL,105,true)
ON CONFLICT (id) DO UPDATE SET
name_vi = EXCLUDED.name_vi,
name_en = EXCLUDED.name_en,
type = EXCLUDED.type,
icon = EXCLUDED.icon,
color = EXCLUDED.color,
default_bucket = EXCLUDED.default_bucket,
sort_order = EXCLUDED.sort_order,
is_mandatory = EXCLUDED.is_mandatory;

CREATE INDEX IF NOT EXISTS idx_tx_customer_date ON transactions(customer_id, transaction_date DESC, id DESC);
CREATE INDEX IF NOT EXISTS idx_tx_wallet ON transactions(wallet_id);
CREATE INDEX IF NOT EXISTS idx_tx_category ON transactions(category_id);
CREATE INDEX IF NOT EXISTS idx_tx_pair ON transactions(transfer_pair_id) WHERE transfer_pair_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_tx_external ON transactions(external_id) WHERE external_id IS NOT NULL;
