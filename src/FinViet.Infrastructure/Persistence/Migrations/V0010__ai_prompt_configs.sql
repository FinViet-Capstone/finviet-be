-- Admin-configurable AI prompt settings.
--
-- Lets an admin tune each Gemini feature's persona instruction, temperature and output-token cap
-- from the admin web instead of the values living as hard-coded constants in GeminiAiModelClient.
-- The non-negotiable safety rules (no fabricated figures, no OTP/credential requests, read-only
-- claims, prompt-injection handling) intentionally do NOT live in this table — they stay a fixed
-- constant in code and are always prepended to the editable persona, so an admin can restyle the
-- assistant but cannot strip its guardrails.
--
-- ai_prompt_config_history keeps one snapshot per state (including the seeded initial state) so
-- the admin screen can show who changed what, when, and revert by re-submitting an old snapshot.

CREATE TABLE public.ai_prompt_configs (
    feature_key character varying(40) NOT NULL PRIMARY KEY,
    display_name character varying(100) NOT NULL,
    persona_instruction text NOT NULL,
    temperature numeric(3,2) NOT NULL,
    max_output_tokens integer NOT NULL,
    updated_by uuid NULL REFERENCES public.admins(id) ON DELETE SET NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_ai_prompt_configs_feature_key
        CHECK (feature_key IN ('chat', 'weekly_report', 'score_comment', 'classification')),
    CONSTRAINT chk_ai_prompt_configs_temperature CHECK (temperature >= 0 AND temperature <= 2),
    CONSTRAINT chk_ai_prompt_configs_max_output_tokens CHECK (max_output_tokens BETWEEN 16 AND 8192)
);

CREATE TABLE public.ai_prompt_config_history (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    feature_key character varying(40) NOT NULL
        REFERENCES public.ai_prompt_configs(feature_key) ON DELETE CASCADE,
    persona_instruction text NOT NULL,
    temperature numeric(3,2) NOT NULL,
    max_output_tokens integer NOT NULL,
    changed_by uuid NULL REFERENCES public.admins(id) ON DELETE SET NULL,
    changed_at timestamp with time zone DEFAULT now() NOT NULL
);

CREATE INDEX idx_ai_prompt_config_history_feature_changed_at
    ON public.ai_prompt_config_history (feature_key, changed_at DESC);

-- Seeds mirror the values previously hard-coded in GeminiAiModelClient (persona split out of the
-- old FinancialSafetyPolicy constant; temperature/max tokens unchanged per feature).
INSERT INTO public.ai_prompt_configs
    (feature_key, display_name, persona_instruction, temperature, max_output_tokens)
VALUES
    ('chat', 'Trợ lý chat',
     E'Bạn là trợ lý tài chính cá nhân của FinViet.\nLuôn trả lời bằng tiếng Việt, giọng thân thiện, tích cực và hữu ích.',
     0.40, 768),
    ('weekly_report', 'Báo cáo tuần',
     E'Bạn là trợ lý tài chính cá nhân của FinViet.\nLuôn trả lời bằng tiếng Việt, giọng thân thiện, tích cực và hữu ích.',
     0.50, 512),
    ('score_comment', 'Nhận xét điểm chi tiêu',
     E'Bạn là trợ lý tài chính cá nhân của FinViet.\nLuôn trả lời bằng tiếng Việt, giọng thân thiện, tích cực và hữu ích.',
     0.50, 160),
    ('classification', 'Phân loại giao dịch',
     E'Bạn là bộ phân loại giao dịch tài chính của FinViet.\nTuân thủ danh sách danh mục đóng và schema đầu ra.',
     0.10, 512);

INSERT INTO public.ai_prompt_config_history
    (feature_key, persona_instruction, temperature, max_output_tokens, changed_by)
SELECT feature_key, persona_instruction, temperature, max_output_tokens, NULL
FROM public.ai_prompt_configs;
