-- FinViet mandatory reference data baseline.
-- Contains stable system-owned rows only. User, admin, transaction, chat, RAG,
-- subscription-plan, and scoring-criteria data are intentionally excluded.
-- This migration is immutable after release.

INSERT INTO public.buckets (id, name_vi, name_en, color, icon, sort_order, is_locked)
VALUES
    ('needs',   'Thiết yếu',  'Needs',   '#d0bcff', 'home',         1, false),
    ('wants',   'Mong muốn',  'Wants',   '#ffb690', 'shopping_bag', 2, false),
    ('savings', 'Tiết kiệm',  'Savings', '#4edea3', 'savings',      3, true)
ON CONFLICT (id) DO UPDATE SET
    name_vi = EXCLUDED.name_vi,
    name_en = EXCLUDED.name_en,
    color = EXCLUDED.color,
    icon = EXCLUDED.icon,
    sort_order = EXCLUDED.sort_order,
    is_locked = EXCLUDED.is_locked;

INSERT INTO public.categories
    (id, name_vi, name_en, type, icon, color, default_bucket, sort_order)
VALUES
    ('cat_food',              'Ăn uống',              'Food',                'expense', 'restaurant',      '#4EDEA3', 'needs',   1),
    ('cat_housing',           'Nhà ở & Tiện ích',     'Housing & Utilities', 'expense', 'home',            '#D0BCFF', 'needs',   2),
    ('cat_transport',         'Di chuyển',            'Transport',           'expense', 'directions_car',  '#90CAF9', 'needs',   3),
    ('cat_health',            'Sức khỏe & Y tế',      'Health',              'expense', 'local_hospital',  '#EF9A9A', 'needs',   4),
    ('cat_education',         'Giáo dục',             'Education',           'expense', 'school',          '#FFE082', 'needs',   5),
    ('cat_family',            'Gửi tiền gia đình',    'Family support',      'expense', 'family_restroom', '#BCAAA4', 'needs',   6),
    ('cat_entertain',         'Giải trí',             'Entertainment',       'expense', 'sports_esports',  '#CE93D8', 'wants',   7),
    ('cat_beauty',            'Quần áo & Thời trang', 'Fashion',             'expense', 'checkroom',       '#F8BBD0', 'wants',   8),
    ('cat_shopping',          'Mua sắm online',       'Shopping',            'expense', 'shopping_bag',    '#FFB690', 'wants',   9),
    ('cat_dining',            'Ăn ngoài & Cà phê',    'Dining & Coffee',     'expense', 'local_cafe',      '#FFCC80', 'wants',  10),
    ('cat_savings',           'Tiết kiệm',            'Savings',             'expense', 'savings',         '#4EDEA3', 'savings', 11),
    ('cat_invest',            'Đầu tư',               'Investment',          'expense', 'trending_up',     '#A5D6A7', 'savings', 12),
    ('cat_savings_goal',      'Mục tiêu tiết kiệm',   'Saving goal',         'expense', 'flag',            '#80CBC4', 'savings', 13),
    ('cat_salary',            'Lương',                'Salary',              'income',  'payments',        '#81C784', NULL,      101),
    ('cat_freelance',         'Freelance',            'Freelance',           'income',  'work',            '#64B5F6', NULL,      102),
    ('cat_investment_return', 'Lợi nhuận đầu tư',     'Investment return',   'income',  'trending_up',     '#A5D6A7', NULL,      103),
    ('cat_gift',              'Quà tặng',             'Gift',                'income',  'redeem',          '#F48FB1', NULL,      104),
    ('cat_income_other',      'Thu nhập khác',        'Other income',        'income',  'more_horiz',      '#B0BEC5', NULL,      105)
ON CONFLICT (id) DO UPDATE SET
    name_vi = EXCLUDED.name_vi,
    name_en = EXCLUDED.name_en,
    type = EXCLUDED.type,
    icon = EXCLUDED.icon,
    color = EXCLUDED.color,
    default_bucket = EXCLUDED.default_bucket,
    sort_order = EXCLUDED.sort_order;
