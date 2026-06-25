// Live demo: rule auto-apply on transaction create + rule precedence over AI in SMS extract.
// Uses unique keywords so it won't clash with previously-seeded rules. Cleans up after itself.
const BASE = process.env.FINVIET_BASE || 'http://localhost:5122';
const CUST = { email: 'tkv2003@gmail.com', password: 'Tkl123tkl123' };
const uuid = () => crypto.randomUUID();
const rnd = Math.floor(Math.random() * 100000);

async function api(method, path, { token, body, headers } = {}) {
  const h = { 'Content-Type': 'application/json', ...(headers || {}) };
  if (token) h.Authorization = 'Bearer ' + token;
  const res = await fetch(BASE + path, { method, headers: h, body: body ? JSON.stringify(body) : undefined });
  let json = null; const text = await res.text();
  try { json = JSON.parse(text); } catch {}
  return { status: res.status, json, text };
}
const data = r => (r.json?.data ?? r.json);

(async () => {
  console.log(`===== DEMO Rules auto-apply @ ${BASE} — account tkv2003@gmail.com =====\n`);
  const cust = data(await api('POST', '/api/auth/login', { body: CUST }))?.accessToken;
  if (!cust) return console.log('Login failed');
  const T = { token: cust };

  const w = await api('POST', '/api/wallets', { ...T, body: { walletName: 'DEMO AUTOAPPLY ' + rnd, walletType: 'basic', initialBalance: 3_000_000 } });
  const wid = data(w)?.walletId;

  const kwCafe = 'CafeDemo' + rnd;     // → cat_dining
  const kwShop = 'ShopDemo' + rnd;     // → cat_shopping
  const r1 = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: kwCafe, categoryId: 'cat_dining' } });
  const r2 = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: kwShop, categoryId: 'cat_shopping' } });
  const rule1 = data(r1)?.rule?.ruleId, rule2 = data(r2)?.rule?.ruleId;
  console.log(`Rules created:  "${kwCafe}"→cat_dining   "${kwShop}"→cat_shopping\n`);

  const mkTx = (note, categoryId) => api('POST', '/api/transactions', { ...T, headers: { 'Idempotency-Key': uuid() },
    body: { walletId: wid, ...(categoryId ? { categoryId } : {}), transactionType: 'EXPENSE', amount: 70000, transactionDate: new Date().toISOString(), note, entryMethod: 'manual' } });

  console.log('----- DEMO 1: tạo giao dịch (POST /api/transactions) -----');
  const t1 = await mkTx(`${kwCafe} Nguyen Hue`);                 // no category → rule applies
  console.log(`  [no category] note="${kwCafe} Nguyen Hue"   → categoryId = ${data(t1)?.categoryId}   (kỳ vọng cat_dining — TỰ GÁN)`);
  const t2 = await mkTx('Quan com binh dan khong rule');         // no category, no rule
  console.log(`  [no category] note="Quan com binh dan..."   → categoryId = ${data(t2)?.categoryId ?? '(null)'}   (không rule → để trống)`);
  const t3 = await mkTx(`${kwCafe} Q1`, 'cat_food');             // user picked category → respected
  console.log(`  [user chọn cat_food] note="${kwCafe} Q1"     → categoryId = ${data(t3)?.categoryId}   (kỳ vọng cat_food — KHÔNG ghi đè lựa chọn user)`);

  console.log('\n----- DEMO 2: extract SMS (POST /api/extract/sms) — rule ưu tiên hơn AI -----');
  const sms = [
    `TK 0123 -250,000 VND luc 12/06/2026 10:00. ND: ${kwShop} thanh toan don hang`,
    '',
    'TK 0123 -45,000 VND luc 12/06/2026 11:30. ND: ca phe sang khong co rule',
  ].join('\n');
  const ex = await api('POST', '/api/extract/sms', { ...T, body: { text: sms } });
  for (const row of (data(ex)?.rows || [])) {
    const tag = row.confidence === 1 ? 'RULE (ưu tiên)' : (row.categoryName ? 'AI' : 'chưa phân loại');
    console.log(`  amount=${row.amount}  desc="${row.description}"  → categoryId=${row.categoryId ?? '(null)'} name=${row.categoryName ?? '(null)'} conf=${row.confidence ?? '(null)'}  [${tag}]`);
  }

  // cleanup
  for (const id of [rule1, rule2]) if (id) await api('DELETE', `/api/rules/${id}`, T);
  if (wid) await api('DELETE', `/api/wallets/${wid}`, T);
  console.log('\n(cleanup done — đã xóa 2 rule + ví demo)');
})();
