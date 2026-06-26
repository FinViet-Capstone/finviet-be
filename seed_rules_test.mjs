// Seed + live test for the Rules feature against the real customer account.
const BASE = 'http://localhost:5122';
const CUST = { email: 'tkv2003@gmail.com', password: 'Tkl123tkl123' };
const ADMIN = { username: 'admin', password: 'Admin@123' };
const uuid = () => crypto.randomUUID();

let cust, admin;
async function api(method, path, { token, body, headers } = {}) {
  const h = { 'Content-Type': 'application/json', ...(headers || {}) };
  if (token) h.Authorization = 'Bearer ' + token;
  const res = await fetch(BASE + path, { method, headers: h, body: body ? JSON.stringify(body) : undefined });
  let json = null; const text = await res.text();
  try { json = JSON.parse(text); } catch {}
  return { status: res.status, json, text };
}
const data = r => (r.json?.data ?? r.json);
const PASS = [], FAIL = [];
function check(name, cond, detail = '') {
  (cond ? PASS : FAIL).push(name);
  console.log(`${cond ? '✅' : '❌'} ${name}${detail ? '  — ' + detail : ''}`);
}

// label helper for txn category lookups
async function catOf(token, txId) {
  const r = await api('GET', `/api/transactions/${txId}`, { token });
  return data(r)?.categoryId ?? '(null)';
}

(async () => {
  console.log('========== RULES FEATURE — SEED & LIVE TEST ==========\n');

  // ---- AUTH ----
  cust = data(await api('POST', '/api/auth/login', { body: CUST }))?.accessToken;
  admin = data(await api('POST', '/api/auth/admin-login', { body: ADMIN }))?.accessToken;
  check('Customer login', !!cust);
  check('Admin login', !!admin);
  if (!cust) { console.log('Cannot continue without customer token.'); return; }
  const T = { token: cust };

  // ---- SEED WALLET ----
  const w = await api('POST', '/api/wallets', { ...T, body: { walletName: 'RULE TEST ' + Date.now(), walletType: 'basic', initialBalance: 5_000_000 } });
  const wid = data(w)?.walletId;
  check('Create test wallet', !!wid, `walletId=${wid}`);
  const w2 = await api('POST', '/api/wallets', { ...T, body: { walletName: 'RULE TEST B ' + Date.now(), walletType: 'basic', initialBalance: 1_000_000 } });
  const wid2 = data(w2)?.walletId;

  // ---- SEED TRANSACTIONS (manual → text stored in description/note; all start as cat_food) ----
  // merchant text is put in `note` because manual create stores description, not merchant.
  const seed = [
    ['A', 'Highlands Coffee Nguyen Hue', 65000],
    ['B', 'Highlands Coffee Q1',         55000],
    ['C', 'Shopee Mall - don hang 9.9',  320000],
    ['D', 'ShopeeFood - Bun bo Hue',     75000],
    ['E', 'Grab car 4.7km',              48000],
    ['F', 'Tiem com tam Kiet',           45000],
  ];
  const tx = {};
  for (const [label, note, amount] of seed) {
    const r = await api('POST', '/api/transactions', { ...T, headers: { 'Idempotency-Key': uuid() },
      body: { walletId: wid, categoryId: 'cat_food', transactionType: 'EXPENSE', amount, transactionDate: new Date().toISOString(), note, entryMethod: 'manual' } });
    tx[label] = data(r)?.transactionId;
  }
  check('Seed 6 transactions (all cat_food)', Object.values(tx).every(Boolean),
    Object.entries(tx).map(([k, v]) => `${k}=${v ? v.slice(0, 8) : 'X'}`).join(' '));

  // a transfer (legs must be skipped by rules)
  await api('POST', '/api/wallets/transfer', { ...T, headers: { 'Idempotency-Key': uuid() },
    body: { fromWalletId: wid, toWalletId: wid2, amount: 100000, description: 'RULEXFER move' } });

  console.log('\n----- GET rules (before) -----');
  const before = await api('GET', '/api/rules', T);
  check('GET /api/rules', before.status === 200, `count=${(data(before) || []).length}`);

  // ---- RULE 1: Highlands → dining (retro-apply A,B) ----
  console.log('\n----- Rule 1: "Highlands" → cat_dining -----');
  const r1 = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: 'Highlands', categoryId: 'cat_dining' } });
  const rule1 = data(r1)?.rule?.ruleId;
  check('Create rule Highlands (201)', r1.status === 201, `appliedCount=${data(r1)?.appliedCount}`);
  check('appliedCount = 2 (A,B)', data(r1)?.appliedCount === 2);
  check('Tx A reclassified → cat_dining', (await catOf(cust, tx.A)) === 'cat_dining');
  check('Tx B reclassified → cat_dining', (await catOf(cust, tx.B)) === 'cat_dining');
  check('Tx F untouched (cat_food)', (await catOf(cust, tx.F)) === 'cat_food');

  // ---- RULE 2: Shopee → shopping (retro-apply C,D) ----
  console.log('\n----- Rule 2: "Shopee" → cat_shopping -----');
  const r2 = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: 'Shopee', categoryId: 'cat_shopping' } });
  const rule2 = data(r2)?.rule?.ruleId;
  check('Create rule Shopee (201)', r2.status === 201, `appliedCount=${data(r2)?.appliedCount}`);
  check('appliedCount = 2 (C,D)', data(r2)?.appliedCount === 2);
  check('Tx C → cat_shopping', (await catOf(cust, tx.C)) === 'cat_shopping');
  check('Tx D → cat_shopping (for now)', (await catOf(cust, tx.D)) === 'cat_shopping');

  // ---- RULE 3: ShopeeFood → dining — LONGEST MATCH WINS (BL §8) ----
  console.log('\n----- Rule 3: "ShopeeFood" → cat_dining  (BL §8 longest-match) -----');
  const r3 = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: 'ShopeeFood', categoryId: 'cat_dining' } });
  const rule3 = data(r3)?.rule?.ruleId;
  check('Create rule ShopeeFood (201)', r3.status === 201, `appliedCount=${data(r3)?.appliedCount}`);
  check('appliedCount = 1 (only D)', data(r3)?.appliedCount === 1);
  check('Tx D → cat_dining (ShopeeFood beats Shopee)', (await catOf(cust, tx.D)) === 'cat_dining');
  check('Tx C stays cat_shopping (only Shopee matches)', (await catOf(cust, tx.C)) === 'cat_shopping');

  // ---- RULE 4: transfer legs are skipped ----
  console.log('\n----- Rule 4: "RULEXFER" → cat_food  (transfer legs must be skipped) -----');
  const r4 = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: 'RULEXFER', categoryId: 'cat_food' } });
  const rule4 = data(r4)?.rule?.ruleId;
  check('Create rule RULEXFER (201)', r4.status === 201, `appliedCount=${data(r4)?.appliedCount}`);
  check('appliedCount = 0 (transfer legs excluded)', data(r4)?.appliedCount === 0);

  // ---- NEGATIVE / GUARD CASES ----
  console.log('\n----- Guards -----');
  const dup = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: 'shopee', categoryId: 'cat_shopping' } });
  check('Duplicate keyword (case-insensitive) → 409', dup.status === 409);
  const badCat = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: 'ZZNope' + Date.now(), categoryId: 'cat_nope' } });
  check('Unknown category → 404', badCat.status === 404);
  const savingsGoal = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: 'ZZGoal' + Date.now(), categoryId: 'cat_savings_goal' } });
  check('cat_savings_goal (auto-only) → 400', savingsGoal.status === 400);
  const empty = await api('POST', '/api/rules', { ...T, body: { merchantKeyword: '   ', categoryId: 'cat_food' } });
  check('Empty keyword → 400', empty.status === 400);
  const adminGet = await api('GET', '/api/rules', { token: admin });
  check('Admin GET /api/rules → 403 (customer-only)', adminGet.status === 403);

  // ---- LIST (after) ----
  console.log('\n----- GET rules (after) -----');
  const after = await api('GET', '/api/rules', T);
  check('GET /api/rules after', after.status === 200, `count=${(data(after) || []).length}`);
  for (const r of (data(after) || [])) console.log(`   • "${r.merchantKeyword}" → ${r.categoryId} (applied ${r.appliedCount})`);

  // ---- DELETE ----
  console.log('\n----- Delete rules -----');
  const del = await api('DELETE', `/api/rules/${rule1}`, T);
  check('Delete rule Highlands → 200', del.status === 200);
  const delMissing = await api('DELETE', '/api/rules/00000000-0000-0000-0000-000000000000', T);
  check('Delete missing rule → 404', delMissing.status === 404);

  // ---- FINAL STATE TABLE ----
  console.log('\n----- Final transaction categories -----');
  for (const [label, note] of seed.map(s => [s[0], s[1]])) {
    console.log(`   ${label}  ${note.padEnd(32)} → ${await catOf(cust, tx[label])}`);
  }

  // ---- CLEANUP ----
  for (const id of [rule2, rule3, rule4]) if (id) await api('DELETE', `/api/rules/${id}`, T);
  await api('DELETE', `/api/wallets/${wid}`, T);
  await api('DELETE', `/api/wallets/${wid2}`, T);

  console.log(`\n========== RESULT: ${PASS.length} passed, ${FAIL.length} failed ==========`);
  if (FAIL.length) console.log('FAILED:', FAIL.join(' | '));
})();
