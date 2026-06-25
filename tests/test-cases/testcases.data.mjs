// Single source of truth for the FinViet test-case document (used by xlsx + docx generators).

export const meta = {
  title: 'FinViet - Backend API Test Cases & Results',
  rows: [
    ['Project', 'FinViet - AI-Powered Personal Finance Tracker (Newest_Dev BE: ASP.NET Core + PostgreSQL)'],
    ['Test type', 'Functional / Integration (HTTP black-box against running API)'],
    ['Environment', 'http://localhost:5122 | DB FinViet_update (v3)'],
    ['Accounts', 'Customer tkv2003@gmail.com | Admin admin'],
    ['Executed', '2026-06-24'],
    ['Automated by', 'tests/FinViet.Api.IntegrationTests (dotnet test)'],
    ['xUnit run result', '55 Passed, 11 Skipped, 0 Failed'],
  ],
};

// [ID, Module, Precondition, Steps, Expected, Status, Notes]
export const cases = [
  ['TC-AUTH-01','Auth','Seeded customer','POST /api/auth/login valid email+password','200, accessToken + refreshToken','Pass',''],
  ['TC-AUTH-02','Auth','Seeded customer','POST /api/auth/login wrong password','401 Unauthorized','Pass',''],
  ['TC-AUTH-03','Auth','Seeded admin','POST /api/auth/admin-login valid creds','200, accessToken','Pass',''],
  ['TC-AUTH-04','Auth','Valid refresh token','POST /api/auth/refresh-token','200, new token pair (rotation)','Pass',''],
  ['TC-PROF-01','Profile','Logged-in customer','GET /api/profile','200, profile with matching email','Pass',''],
  ['TC-PROF-02','Profile','Logged-in customer','PUT /api/profile monthlyIncome=12M','200, income persisted','Pass',''],
  ['TC-PROF-03','Profile','No token','GET /api/profile','401 Unauthorized','Pass',''],

  ['TC-CAT-01','Category','Seeded library','GET /api/categories','200, >=14 categories','Pass',''],
  ['TC-CAT-02','Category','-','GET /api/categories?type=expense','200, only expense','Pass',''],
  ['TC-CAT-03','Category','-','GET /api/categories?type=income','200, only income','Pass',''],
  ['TC-CAT-04','Category','-','GET /api/categories/cat_food','200, category returned','Pass',''],
  ['TC-CAT-05','Category','-','GET /api/categories/cat_does_not_exist','404 Not Found','Pass',''],

  ['TC-WAL-01','Wallet','Logged-in','GET /api/wallets','200, totalBalance + wallets[]','Pass',''],
  ['TC-WAL-02','Wallet','Logged-in','POST /api/wallets basic balance 1M','201, balance = 1M','Pass',''],
  ['TC-WAL-03','Wallet','Logged-in','POST /api/wallets walletType=nonsense','400 (only basic/sepay_linked)','Pass',''],
  ['TC-WAL-04','Wallet','Wallet exists','PATCH /api/wallets/{id} rename','200, name updated','Pass',''],
  ['TC-WAL-05','Wallet','Wallet exists','PATCH /api/wallets/{id} change type','400 (type immutable, BL §1)','Pass',''],
  ['TC-WAL-06','Wallet','No token','GET /api/wallets','401 Unauthorized','Pass',''],

  ['TC-TXN-01','Transaction','Logged-in','GET /api/transactions?page=1&pageSize=10','200, paged items','Pass',''],
  ['TC-TXN-02','Transaction','Wallet 1M','POST /api/transactions expense 50k','201, wallet debited to 950k','Pass',''],
  ['TC-TXN-03','Transaction','Wallet 10k','POST expense 999,999,999','422 insufficient balance','Pass',''],
  ['TC-TXN-04','Transaction','Wallet 1M','POST twice same Idempotency-Key','Single tx, debit once (965k)','Pass','Dedup confirmed enforced'],
  ['TC-TXN-05','Transaction','Tx exists','PATCH /classify then PUT category','200 both','Pass',''],
  ['TC-TXN-06','Dashboard','-','GET /api/transactions/summary','200, byCategory+byDay+topBeneficiaries','Pass','donut/bar/top-5 data'],

  ['TC-TRF-01','Transfer','2 wallets','POST /api/wallets/transfer 100k','200, balances move 900k/600k','Pass',''],
  ['TC-TRF-02','Transfer','1 wallet','POST transfer same from/to','400 must differ','Pass',''],
  ['TC-TRF-03','Transfer','Wallet 10k','POST transfer 999,999,999','400 insufficient balance','Pass',''],
  ['TC-WDR-01','Withdraw','Basic wallet','POST /api/wallets/withdraw from basic','422 (SePay-linked only, BL §1)','Pass',''],

  ['TC-BUD-01','Budget','Logged-in','GET /api/budgets','200','Pass',''],
  ['TC-BUD-02','Budget','Logged-in','GET /api/budgets/buckets','200, buckets+monthlyIncome+allocationCap','Pass','BL §6'],
  ['TC-BUD-03','Budget','-','POST -> PATCH -> DELETE budget','200/200/204','Pass',''],
  ['TC-BUD-04','Budget','Admin token','GET /api/budgets as admin','403 (customer-only)','Pass',''],

  ['TC-GOL-01','Goal','Wallet exists','Create->Get->Patch->Contribute->Delete','201/200/200/200, currentAmount=200k','Pass',''],
  ['TC-GOL-02','Goal','Goal target 500k','Contribute 999,999,999','422 exceeds remaining (BL §10)','Pass',''],

  ['TC-EXT-01','SMS import','Logged-in','POST /api/extract/sms valid text','200, >=1 row (amount/merchant/date)','Pass',''],
  ['TC-EXT-02','SMS import','-','POST /api/extract/sms empty','400','Pass',''],
  ['TC-EXT-03','CSV import','-','POST /api/extract/csv plain .csv','200 parsed rows','BUG #C','500 "Invalid file signature": parser Excel-only'],

  ['TC-AI-01','Spending Score','Logged-in','GET /api/ai/score?period=WEEKLY','200, finalScore (weights 50/50)','Pass',''],
  ['TC-AI-02','Spending Score','Logged-in','GET /api/ai/score?period=MONTHLY','200, finalScore (30/40/30)','Pass',''],
  ['TC-AI-03','Categorize','Logged-in','POST /api/ai/categorize/preview','200 suggestion','ISSUE #B','500: Gemini API key not configured'],
  ['TC-AI-04','Weekly Report','Logged-in','GET /api/ai/reports','200 list','BUG #A','500 column a.report_id does not exist'],
  ['TC-AI-05','Weekly Report','Logged-in','POST /api/ai/reports/generate','2xx','BUG #A','500 (v3 schema drift)'],
  ['TC-AI-06','Chatbot','Logged-in','POST /api/ai/chat question','200 answer','BUG #A','500: relation chat_message missing'],
  ['TC-AI-07','Chatbot','Logged-in','GET /api/ai/chat/history','200','BUG #A','500: chat_message missing'],

  ['TC-REQ-01','Cat request','Customer','POST /api/category-requests then GET /mine','200/201 + visible','Pass',''],
  ['TC-REQ-02','Cat request','Admin + pending','POST /api/category-requests/{id}/approve','200, global category created','Pass',''],
  ['TC-REQ-03','Cat request','Admin + pending','POST /api/category-requests/{id}/reject','200','Pass',''],
  ['TC-ADM-01','Admin Category','Admin','POST->PATCH->DELETE /api/categories','201/200/200','Pass',''],
  ['TC-ADM-02','Admin Account','Admin','PUT /api/account/deactivate/{unknown}','404','Pass',''],

  ['TC-AUTHZ-01','RBAC','Customer token','GET /api/category-requests (admin-only)','403','Pass',''],
  ['TC-AUTHZ-02','RBAC','Customer token','POST /api/categories (admin-only)','403','Pass',''],
  ['TC-AUTHZ-03','RBAC','Admin token','GET /api/wallets (customer-only)','403','Pass',''],
  ['TC-AUTHZ-04','RBAC','No token','GET /api/wallets','401','Pass',''],

  ['TC-RULE-01','Rules','Logged-in','GET /api/rules','200, list','Pass','api_list #41'],
  ['TC-RULE-02','Rules','Tx w/ keyword','POST /api/rules then retro-apply','201, appliedCount>=1, tx category updated','Pass','BL §2 hồi tố; api_list #42'],
  ['TC-RULE-03','Rules','Rule exists','POST /api/rules duplicate keyword','409 Conflict','Pass','case-insensitive'],
  ['TC-RULE-04','Rules','-','POST /api/rules unknown category','404 Not Found','Pass',''],
  ['TC-RULE-05','Rules','-','DELETE /api/rules/{missing}','404 Not Found','Pass','api_list #43'],
  ['TC-RULE-06','Rules','Admin token','GET /api/rules as admin','403 (customer-only)','Pass',''],
  ['TC-RULE-07','Rules','Rule exists','POST /api/transactions không có category, note khớp rule','201, tx tự gán category của rule','Pass','BL §2b auto-apply khi tạo'],
  ['TC-RULE-08','Rules','Rule exists','POST /api/extract/sms text khớp rule','row.categoryId = rule, confidence 1.0 (rule ưu tiên hơn AI)','Pass','BL §2b precedence'],

  ['TC-GAP-01','Admin Analytics','-','System analytics: users/DAU/tx/AI-cost per day','Endpoint available','Not Implemented','No endpoint'],
  ['TC-GAP-02','Admin Corr.Log','-','Category correction log VIEW','Endpoint available','Not Implemented','Write path only'],
  ['TC-GAP-03','Admin Announce','-','Announcement broadcast to users/segments','Endpoint available','Not Implemented','No endpoint'],
  ['TC-GAP-04','Admin Users','-','List users / re-activate / admin reset password','Endpoints available','Partial','Only deactivate exists'],
  ['TC-GAP-05','Notifications','-','Notification center (list/read)','Endpoint available','Not Implemented','No endpoint'],
];

// [#, Severity, Area, Symptom, Root cause, Suggested fix]
export const defects = [
  ['A','High','AI Weekly Report + Chatbot','500 on /api/ai/reports* and /api/ai/chat*','DbInitializer skips all SQL migrations on v3 schema -> V8 (ai_weekly_reports.report_id, chat_message) never ran on FinViet_update; EF model drifted from DB','Align v3 schema with EF model (add report_id, create chat_message) or run V8 backfill'],
  ['B','Medium','AI categorization','500 "Gemini API key is not configured"','Missing Gemini API key in config (spec says OpenAI, impl uses Gemini)','Configure Gemini key; document provider deviation'],
  ['C','Medium','CSV import','500 "Invalid file signature" on plain CSV','BankStatementExcelParser only reads Excel binaries; endpoint accepts .csv but cannot parse text','Add a CSV text parser; return 400 (not 500) on unsupported format'],
];

export const counts = () => ({
  total: cases.length,
  pass: cases.filter(c => c[5] === 'Pass').length,
  bug: cases.filter(c => /BUG|ISSUE/.test(c[5])).length,
  notImpl: cases.filter(c => /Not Implemented|Partial/.test(c[5])).length,
});
