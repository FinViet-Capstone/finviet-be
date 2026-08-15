-- Seeds public.scoring_criteria, left empty since V0002. Values match the weights that were
-- previously hardcoded in SpendingScoreService.ComputeAsync (spike/budget weekly, spike/budget/
-- savings monthly) so wiring the service up to read from this table is behavior-preserving.

INSERT INTO public.scoring_criteria (code, name_vi, weight_weekly, weight_monthly)
VALUES
    ('spike',   'Đột biến chi tiêu',  50.00, 30.00),
    ('budget',  'Tuân thủ ngân sách', 50.00, 40.00),
    ('savings', 'Tiết kiệm',           0.00, 30.00)
ON CONFLICT (code) DO UPDATE SET
    name_vi = EXCLUDED.name_vi,
    weight_weekly = EXCLUDED.weight_weekly,
    weight_monthly = EXCLUDED.weight_monthly;
