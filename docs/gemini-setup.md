# Cấu hình Gemini cho FinViet

FinViet dùng official `Google.GenAI` .NET SDK cho phân loại giao dịch, chat read-only, nhận xét điểm, báo cáo tuần và embedding RAG.

## 1. Model và cấu hình mặc định

| Mục đích | Giá trị mặc định |
|---|---|
| Generation/classification | `gemini-3.6-flash` |
| Embedding | `gemini-embedding-001` |
| Kích thước embedding | `768` |
| Timeout | `120` giây |
| RAG | Tắt cho đến khi re-index hoàn tất |
| Ngưỡng similarity | `0.72` |

Cột `rag_chunk.embedding` là `vector(768)`, vì vậy `Gemini:EmbeddingDimensions` bắt buộc bằng 768.

## 2. API key

Không ghi API key vào `appsettings*.json`, source code, tài liệu, log hoặc Git.

Cấu hình local bằng .NET user-secrets:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<gemini-api-key>" --project src/FinViet.Api
```

Hoặc dùng biến môi trường:

```powershell
$env:Gemini__ApiKey = "<gemini-api-key>"
```

Các biến môi trường tùy chọn:

```text
Gemini__FlashModel=gemini-3.6-flash
Gemini__EmbeddingModel=gemini-embedding-001
Gemini__EmbeddingDimensions=768
Gemini__TimeoutSeconds=120
Gemini__RagEnabled=false
Gemini__RagMinimumSimilarity=0.72
```

Startup sẽ thất bại sớm nếu thiếu API key, model, dimension không bằng 768, timeout ngoài 5–600 giây hoặc similarity ngoài `[0,1]`.

## 3. Chạy API

Cần PostgreSQL và `ConnectionStrings:DefaultConnection` như bình thường:

```powershell
dotnet restore FinViet.sln
dotnet run --project src/FinViet.Api
```

Development mặc định:

- API: `http://localhost:5122`
- Swagger: `http://localhost:5122/swagger`

Không cần cài hoặc chạy Ollama.

## 4. Kiểm tra cơ bản

1. Đăng nhập qua `POST /api/auth/login`.
2. Dán raw `accessToken` vào Swagger Authorize.
3. Giữ AI categorization ở `suggest_only` trong giai đoạn đầu.
4. Gọi `POST /api/ai/categorize/preview`:

```json
{
  "input": "Grab 4.7km"
}
```

5. Tạo một chat session qua `POST /api/ai/chat/sessions`, sau đó gọi `POST /api/ai/chat` với `sessionId`.
6. Kiểm tra response có `dataPeriod`, `citations` và `limitations`.
7. Kiểm tra session `historyEnabled=false` không trả nội dung trong history.

Chat chỉ phân tích và đề xuất. Nó không tạo/sửa/xóa giao dịch, ví, ngân sách, mục tiêu hoặc thực hiện chuyển tiền; không cấu hình Gemini tools/function calling cho chat.

## 5. Preferences

Khách hàng có thể đọc/cập nhật AI preferences qua:

- `GET /api/profile/ai-preferences`
- `PATCH /api/profile/ai-preferences`

Các nhóm cấu hình gồm categorization mode và threshold, mặc định lưu history, tạo báo cáo tuần, quyền dùng balances/transactions/budgets/goals/reports và RAG.

## 6. Chuyển embedding RAG sang Gemini

Embedding cũ từ Ollama/`nomic-embed-text` không tương thích ngữ nghĩa với `gemini-embedding-001`, dù cùng 768 chiều. Không bật RAG trước khi re-index.

> Re-index cập nhật embedding tại chỗ. Đây là thao tác destructive-adjacent: phải sao lưu và phải có xác nhận riêng trước khi chạy.

### Bước 1: Giữ RAG tắt

```text
Gemini__RagEnabled=false
```

Chat vẫn hoạt động bằng dữ liệu tài chính aggregate do backend tính, nhưng không truy xuất vector.

### Bước 2: Kiểm tra corpus

```powershell
psql -h localhost -U admin -d Finviet_update -c "SELECT COUNT(*) AS documents FROM rag_document; SELECT COUNT(*) AS chunks FROM rag_chunk;"
```

Điều chỉnh host, user và database theo connection string thực tế.

### Bước 3: Sao lưu PostgreSQL

```powershell
pg_dump `
    -h localhost `
    -U admin `
    -d Finviet_update `
    -Fc `
    -f "D:\backup\finviet-before-gemini-reindex.dump"
```

Xác nhận file backup tồn tại và có dung lượng hợp lý.

### Bước 4: Re-index sau khi được xác nhận riêng

```powershell
dotnet run --project src/FinViet.Api -- --reindex-rag --confirm-reindex
```

Lệnh sẽ từ chối chạy nếu thiếu `--confirm-reindex` hoặc nếu `Gemini:RagEnabled=true`; nó giữ nguyên document/chunk, thay embedding, kiểm tra corpus không đổi trong lúc xử lý và rebuild pgvector index.

Nếu lỗi hoặc bị gián đoạn, giữ RAG tắt, xử lý nguyên nhân rồi chạy lại sau khi bảo đảm backup còn hợp lệ.

### Bước 5: Kiểm tra rồi bật RAG

1. So sánh lại số document/chunk.
2. Chạy API và kiểm tra sample retrieval/citations.
3. Bật `Gemini__RagEnabled=true`.
4. Restart API và canary với một nhóm nhỏ trước khi rollout rộng.

## 7. Rollout khuyến nghị

1. Sao lưu database rồi deploy V25/additive schema và Gemini secret, RAG tắt. Startup dùng advisory lock và sẽ dừng hẳn nếu schema legacy thiếu dữ liệu bắt buộc hoặc có trùng kỳ báo cáo/điểm; V25 không tự xóa các bản ghi tài chính trùng.
2. Smoke test deterministic chat và provider fallback.
3. Giữ categorization mặc định `suggest_only`.
4. Re-index RAG chỉ sau backup và xác nhận riêng.
5. Bật RAG canary; theo dõi rate-limit, provider errors và citation relevance.
6. Chỉ khách hàng tự bật `high_confidence_auto`; manual và merchant rules luôn được ưu tiên.

## 8. Usage và audit metadata

Mỗi lần gọi Gemini ghi best-effort vào `ai_usage_events`: feature, provider/model, outcome,
latency, token counts và response ID khi SDK trả về. Các quyết định preference/categorization,
report fallback, RAG skip/failure và vòng đời chat session ghi vào `ai_audit_events`.

Hai bảng này không lưu API key, prompt, câu hỏi/câu trả lời, số dư, giao dịch thô hoặc nội dung
tài liệu. Lỗi ghi telemetry chỉ được log cảnh báo và không làm hỏng thao tác của khách hàng.

## 9. Lỗi thường gặp

- **Startup báo thiếu `Gemini:ApiKey`**: cấu hình user-secret hoặc `Gemini__ApiKey`, rồi restart process.
- **Startup dừng do V25 phát hiện dữ liệu legacy không tương thích/trùng kỳ**: không bỏ qua lỗi và không xóa trực tiếp; sao lưu, kiểm tra các hàng được nêu trong lỗi, reconciliation có chủ đích rồi khởi động lại.
- **Embedding dimension không hợp lệ**: phải để 768; đổi dimension yêu cầu thay schema pgvector và nằm ngoài runbook này.
- **Chat có limitation “không có tài liệu RAG”**: kiểm tra global `Gemini:RagEnabled`, preference RAG của customer, corpus đã re-index và `RagMinimumSimilarity`.
- **Provider timeout/quota**: chat trả fallback thân thiện; báo cáo tuần vẫn tạo deterministic fallback. Kiểm tra quota/project trên Gemini và durable AI rate limits.
- **Không có push báo cáo**: kiểm tra Firebase và `CustomerSetting.NotifReport`; báo cáo vẫn được tạo khi push bị tắt hoặc lỗi.
