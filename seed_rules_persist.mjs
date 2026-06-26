// Seed the Rules feature and KEEP the data so it is visible in pgAdmin (merchant_rules).
// Re-runnable: reuses the demo wallet, refreshes the target rules.
const BASE = 'http://localhost:5122';
const CUST = { email: 'tkv2003@gmail.com', password: 'Tkl123tkl123' };
const uuid = () => crypto.randomUUID();

async function api(method, path, { token, body, headers } = {}) {
  const h = { 'Content-Type': 'application/json', ...(headers || {}) };
  if (token) h.Authorization = 'Bearer ' + token;
  const res = await fetch(BASE + path, { method, headers: h, body: body ? JSON.stringify(body) : undefined });
  let json = null; const text = await res.text();
  try { json = JSON.parse(text); } catch {}
  return { status: res.status, json, text };
}
const data = r => (r.json?.data ?? r.json);
const WALLET_NAME = 'RULE DEMO';
const TARGET_KEYWORDS = ['Highlands', 'Shopee', 'ShopeeFood', 'Grab'];

(async () => {
  console.log('===== SEED RULES (persist) — account tkv2003@gmail.com =====\n');
  const cust = data(await api('POST', '/api/auth/login', { body: CUST }))?.accessToken;
  if (!cust) { console.log('Login failed'); return; }
  const T = { token: cust };

  // 1) Reuse or create the demo wallet.
  const wl = await api('GET', '/api/wallets', T);
  let wallet = (data(wl)?.wallets || []).find(w => w.walletName === WALLET_NAME);
  if (!wallet) {
    const created = await api('POST', '/api/wallets', { ...T, body: { walletName: WALLET_NAME, walletType: 'basic', initialBalance: 5_000_000 } });
    wallet = data(created);
    console.log(`Wallet created: ${WALLET_NAME} (${wallet.walletId})`);
  } else {
    console.log(`Wallet reused: ${WALLET_NAME} (${wallet.walletId})`);
  }
  const wid = wallet.walletId;

  // 2) Seed transactions only if the wallet has none yet (avoid duplicates on re-run).
  const existingTx = await api('GET', `/api/wallets/${wid}/transactions?page=1&pageSize=50`, T);
  const txCount = data(existingTx)?.totalItems ?? (data(existingTx)?.items || []).length;
  if (!txCount) {
    const seed = [
      ['Highlands Coffee Nguyen Hue', 65000],
      ['Highlands Coffee Q1',         55000],
      ['Shopee Mall - don hang 9.9',  320000],
      ['ShopeeFood - Bun bo Hue',     75000],
      ['Grab car 4.7km',              48000],
    ];
    for (const [note, amount] of seed) {
      await api('POST', '/api/transactions', { ...T, headers: { 'Idempotency-Key': uuid() },
        body: { walletId: wid, categoryId: 'cat_food', transactionType: 'EXPENSE', amount, transactionDate: new Date().toISOString(), note, entryMethod: 'manual' } });
    }
    console.log(`Seeded ${seed.length} transactions (all cat_food).`);
  } else {
    console.log(`Wallet already has ${txCount} transactions — skipping seed.`);
  }

  // 3) Refresh target rules: delete any existing with these keywords, then create fresh.
  const before = data(await api('GET', '/api/rules', T)) || [];
  for (const r of before) {
    if (TARGET_KEYWORDS.some(k => k.toLowerCase() === r.merchantKeyword.toLowerCase())) {
      await api('DELETE', `/api/rules/${r.ruleId}`, T);
    }
  }

  // Order matters for the §8 demo: create Shopee BEFORE ShopeeFood.
  const ruleSpec = [
    ['Highlands',  'cat_dining'],
    ['Shopee',     'cat_shopping'],
    ['ShopeeFood', 'cat_dining'],
    ['Grab',       'cat_transport'],
  ];
  console.log('\nCreating rules (left in DB):');
  for (const [keyword, categoryId] of ruleSpec) {
    const r = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: keyword, categoryId } });
    const applied = data(r)?.appliedCount;
    console.log(`  ${r.status === 201 ? '✅' : '❌ ' + r.status}  "${keyword}" → ${categoryId}  (applied ${applied})`);
  }

  // 4) Show the persisted rules.
  const after = data(await api('GET', '/api/rules', T)) || [];
  console.log(`\nmerchant_rules now has ${after.length} rule(s) for this customer:`);
  for (const r of after) console.log(`  • ${r.ruleId}  "${r.merchantKeyword}" → ${r.categoryId}  (applied ${r.appliedCount})`);

  console.log('\n>>> Verify in pgAdmin:  SELECT * FROM public.merchant_rules ORDER BY created_at DESC;');
  console.log('>>> Data is LEFT in place (not cleaned up).');
})();
