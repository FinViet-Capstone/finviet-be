// Single source of truth for isolated, executable business-logic test planning.
// Status is intentionally planning-only: generation is not evidence of execution.

export const meta = {
  title: 'FinViet - Unit Business-Logic Test Cases',
  subtitle: 'Auth, Profile, Category, and Wallet',
  rows: [
    ['Scope', 'Isolated unit/business-logic tests; mock collaborators or use an EF Core test double where query behavior requires it.'],
    ['Status policy', 'Executable cases are marked Pass only after the isolated xUnit suite succeeds. DB/API/provider-only scenarios remain Deferred.'],
    ['Target projects', 'FinViet.Application.UnitTests; service/handler tests reference FinViet.Infrastructure.'],
    ['Execution boundary', 'No API host, HTTP requests, Npgsql connection, real PostgreSQL, migrations, seed data, or provider network calls.'],
    ['Executed', '2026-07-31'],
    ['Generated artifacts', 'Unit_BusinessLogic_TestCases.xlsx and Unit_BusinessLogic_TestCases.docx beside these scripts.'],
  ],
};

// [Group, Function, Responsibility / entry point, Isolated test target, Coverage focus]
export const functions = [
  ['Auth', 'RegisterCommandHandler.Handle', 'Register account and send verification email', 'Handler + mocked DbContext/email collaborator', 'duplicate email, normalized data, verification token, send outcome'],
  ['Auth', 'VerifyEmailCommandHandler.Handle', 'Consume a verification token', 'Handler + token/customer fixture', 'missing, expired, used, successful verification'],
  ['Auth', 'GoogleLoginCommandHandler.Handle', 'Verify Firebase ID token and issue local tokens', 'Handler + mocked IFirebaseAuthService/login helper', 'invalid token, user matching, active state, email verification'],
  ['Auth', 'LoginCommandHandler.Handle / IssueTokensAsync', 'Password login and token issuance', 'Handler + password/token dependencies', 'credential rejection, inactive/unverified user, claims/tokens'],
  ['Auth', 'RefreshTokenCommandHandler.Handle', 'Rotate a refresh token', 'Handler + refresh-token fixture', 'missing/revoked/expired token and rotation'],
  ['Auth', 'LogoutCommandHandler.Handle', 'Revoke a refresh token', 'Handler + token fixture', 'revocation and unknown-token behavior'],
  ['Auth', 'ForgotPasswordCommandHandler.Handle / ResetPasswordCommandHandler.Handle', 'Issue and consume password-reset token', 'Handlers + mocked email collaborator', 'privacy response, token validity, password update/revocation'],
  ['Profile', 'GetProfileQueryHandler.Handle', 'Return active customer profile', 'Handler + customer fixture', 'mapping and inactive/missing customer'],
  ['Profile', 'UpdateProfileCommandHandler.Handle', 'Update editable profile fields', 'Handler + customer fixture', 'name/income validation, partial updates, optional fields'],
  ['Profile', 'UploadAvatarCommandHandler.Handle', 'Validate, replace, and persist avatar URL', 'Handler + mocked IAvatarService', 'media type, size, signatures, storage/persistence failures'],
  ['Profile', 'DeleteAccountCommandHandler.Handle', 'Soft-delete customer and revoke refresh tokens', 'Handler + account/token fixture', 'delete state and token revocation'],
  ['Category', 'CategoryService.GetCategoriesAsync / GetCategoryByIdAsync', 'List/filter global categories and resolve customer override', 'Service + category/override fixture', 'type normalization, savings-goal hiding, override precedence'],
  ['Category', 'CategoryService.CreateCategoryAsync', 'Create a global category', 'Service + category fixture', 'normalization, slug, duplicate/type/bucket rules'],
  ['Category', 'CategoryService.UpdateCategoryAsync', 'Patch a global category', 'Service + category fixture', 'name/type/bucket updates and invariants'],
  ['Category', 'CategoryService.DeleteCategoryAsync', 'Delete unused global category', 'Service + category/transaction fixture', 'not found, reference protection, delete behavior'],
  ['Category', 'CategoryService.SetCustomerBucketAsync / ResetCustomerBucketAsync', 'Set/reset customer expense bucket override', 'Service + category/customer-category fixture', 'valid buckets, savings-goal/income protection, upsert/reset'],
  ['Wallet', 'WalletService.CreateWalletAsync', 'Create a basic wallet', 'Service + wallet/customer fixture', 'input/type/name/count rules and opening balance'],
  ['Wallet', 'WalletService.GetWalletsAsync / GetWalletByIdAsync', 'List customer wallets and fetch owned wallet', 'Service + wallet fixture', 'soft-delete filter, total, ownership, mapping'],
  ['Wallet', 'WalletService.UpdateWalletAsync', 'Rename an existing wallet', 'Service + wallet fixture', 'empty/duplicate name and immutable type'],
  ['Wallet', 'WalletService.DeleteWalletAsync', 'Soft-delete wallet subject to policy', 'Service + wallet fixture', 'not found, last-wallet rule, deletion policy'],
];

// [ID, Group, Function, Preconditions / doubles, Test action, Expected isolated assertion, Status, Notes]
export const cases = [
  ['UT-AUTH-01', 'Auth', 'RegisterCommandHandler.Handle', 'Unique email; mock email sender', 'Register a valid command', 'Creates active unverified customer, hashes password, creates verification token, and requests email send.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-02', 'Auth', 'RegisterCommandHandler.Handle', 'Existing email with different casing', 'Register same logical email', 'Throws conflict; does not add customer or send email.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-03', 'Auth', 'RegisterCommandValidator', 'Validator instance', 'Validate blank/invalid email, weak password, and >100-character name', 'Returns the documented validation errors.', 'Pass', 'Executable validator case'],
  ['UT-AUTH-04', 'Auth', 'VerifyEmailCommandHandler.Handle', 'Valid unused verification token and active customer', 'Verify token', 'Sets IsEmailVerified and EmailVerifiedAt and marks token used.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-05', 'Auth', 'VerifyEmailCommandHandler.Handle', 'Missing, expired, and previously-used token fixtures', 'Verify each token', 'Rejects each invalid token without changing customer verification state.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-06', 'Auth', 'GoogleLoginCommandHandler.Handle', 'Firebase returns null', 'Google login', 'Throws UnauthorizedException and does not persist customer or issue tokens.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-07', 'Auth', 'GoogleLoginCommandHandler.Handle', 'Verified Firebase user; no matching customer; mocked token issuer', 'Google login', 'Creates customer with normalized email/Google ID and delegates token issuance.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-08', 'Auth', 'GoogleLoginCommandHandler.Handle', 'Existing inactive Google/email match', 'Google login', 'Throws ForbiddenException; no token issuance.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-09', 'Auth', 'LoginCommandHandler.Handle', 'Active verified customer with known BCrypt hash', 'Login with correct and wrong password', 'Correct password issues response; wrong password rejects without issuing tokens.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-10', 'Auth', 'LoginCommandHandler.Handle', 'Inactive and unverified customer fixtures', 'Login', 'Rejects each account state according to handler rule.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-11', 'Auth', 'RefreshTokenCommandHandler.Handle', 'Active, unexpired token fixture and token issuer', 'Refresh token', 'Revokes old token, persists replacement, and returns new pair.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-12', 'Auth', 'RefreshTokenCommandHandler.Handle', 'Missing, revoked, expired token fixtures', 'Refresh each token', 'Rejects without changing token state or issuing a pair.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-13', 'Auth', 'LogoutCommandHandler.Handle', 'Active refresh token', 'Logout', 'Marks token revoked and saves once.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-14', 'Auth', 'ForgotPasswordCommandHandler.Handle', 'Existing and absent email fixtures; mock sender', 'Request reset for both', 'Uses privacy-preserving response; existing customer receives reset workflow only.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-15', 'Auth', 'ResetPasswordCommandHandler.Handle', 'Valid reset token/customer and token fixture', 'Reset to a valid new password', 'Updates password hash and consumes/revokes applicable reset material.', 'Pass', 'Executable isolated handler case'],
  ['UT-AUTH-16', 'Auth', 'ResetPasswordCommandHandler.Handle', 'Invalid/expired/used token fixtures', 'Reset password', 'Rejects and preserves password/token state.', 'Pass', 'Executable isolated handler case'],

  ['UT-PROF-01', 'Profile', 'GetProfileQueryHandler.Handle', 'Active customer with optional profile fields', 'Get profile', 'Maps identity, email, avatar, gender, DOB, income, and onboarding fields.', 'Pass', 'Executable isolated handler case'],
  ['UT-PROF-02', 'Profile', 'GetProfileQueryHandler.Handle', 'Missing or inactive customer', 'Get profile', 'Throws NotFoundException.', 'Pass', 'Executable isolated handler case'],
  ['UT-PROF-03', 'Profile', 'UpdateProfileCommandHandler.Handle', 'Active customer', 'Update valid name and income', 'Trims/persists supplied values and maps updated profile.', 'Pass', 'Executable isolated handler case'],
  ['UT-PROF-04', 'Profile', 'UpdateProfileCommandValidator', 'Validator instance', 'Validate blank/oversize name and negative income', 'Returns expected failures; valid optional income succeeds.', 'Pass', 'Executable validator case'],
  ['UT-PROF-05', 'Profile', 'UploadAvatarCommandHandler.Handle', 'Active customer; mocked IAvatarService', 'Upload valid JPEG, PNG, and WebP byte signatures', 'Accepts each declared type and persists returned URL.', 'Pass', 'Executable isolated handler case'],
  ['UT-PROF-06', 'Profile', 'UploadAvatarCommandHandler.Handle', 'Active customer; mocked IAvatarService', 'Upload unsupported MIME, mismatched magic bytes, short content, and >5MB payload', 'Throws BadRequestException and does not upload/save.', 'Pass', 'Executable isolated handler case'],
  ['UT-PROF-07', 'Profile', 'UploadAvatarCommandHandler.Handle', 'Customer with prior URL; mocked IAvatarService', 'Replace avatar', 'Requests prior asset deletion, uploads replacement, then stores returned URL.', 'Pass', 'Executable current-order characterization'],
  ['UT-PROF-08', 'Profile', 'DeleteAccountCommandHandler.Handle', 'Active customer with multiple refresh tokens', 'Delete account', 'Soft-deactivates account and revokes every refresh token.', 'Pass', 'Executable isolated handler case'],

  ['UT-CAT-01', 'Category', 'CategoryService.GetCategoriesAsync', 'Income, expense, savings-goal, and override fixtures', 'List with null, mixed-case income, and mixed-case expense filter', 'Normalizes filter; excludes cat_savings_goal; applies active customer override.', 'Pass', 'Executable service case'],
  ['UT-CAT-02', 'Category', 'CategoryService.GetCategoryByIdAsync', 'Known category and override fixture', 'Get known and unknown ID', 'Maps override for known category and returns null for unknown ID.', 'Pass', 'Executable service case'],
  ['UT-CAT-03', 'Category', 'CategoryService.CreateCategoryAsync', 'Empty category fixture', 'Create valid expense with bucket and valid income without bucket', 'Normalizes values; creates deterministic slug when omitted; returns response.', 'Pass', 'Executable service case'],
  ['UT-CAT-04', 'Category', 'CategoryService.CreateCategoryAsync', 'Existing ID/name fixtures', 'Create duplicate ID/name, invalid type, and expense missing/invalid bucket', 'Rejects each invalid request without adding a category.', 'Pass', 'Executable service case'],
  ['UT-CAT-05', 'Category', 'CategoryService.UpdateCategoryAsync', 'Existing category plus same-type duplicate name fixture', 'Patch valid name/bucket/metadata and then duplicate/empty name', 'Persists valid patch; rejects empty or duplicate logical name.', 'Pass', 'Executable service case'],
  ['UT-CAT-06', 'Category', 'CategoryService.DeleteCategoryAsync', 'Known unused, referenced, and absent category fixtures', 'Delete each category', 'Deletes only unused category; rejects reference; absent returns false.', 'Pass', 'Executable service case'],
  ['UT-CAT-07', 'Category', 'CategoryService.SetCustomerBucketAsync', 'Expense category; no existing override', 'Set Needs with casing/whitespace', 'Creates active override with normalized bucket and returns override response.', 'Pass', 'Executable service case'],
  ['UT-CAT-08', 'Category', 'CategoryService.SetCustomerBucketAsync', 'Income, savings-goal, missing-category, and invalid-bucket fixtures', 'Set override', 'Rejects each prohibited request with no override write.', 'Pass', 'Executable service case'],
  ['UT-CAT-09', 'Category', 'CategoryService.ResetCustomerBucketAsync', 'Active override then no override fixture', 'Reset override', 'Deactivates active override; returns global default in both cases.', 'Pass', 'Executable service case'],

  ['UT-WAL-01', 'Wallet', 'WalletService.CreateWalletAsync', 'Existing customer; empty wallet set', 'Create basic wallet with aliases and nonnegative opening balance', 'Normalizes allowed aliases to basic, trims name, and returns mapped wallet.', 'Pass', 'Executable service case'],
  ['UT-WAL-02', 'Wallet', 'WalletService.CreateWalletAsync', 'Existing customer', 'Create blank-name/type, invalid type, and negative-balance requests', 'Rejects each invalid request before persistence.', 'Pass', 'Executable service case'],
  ['UT-WAL-03', 'Wallet', 'WalletService.CreateWalletAsync', 'Ten active wallets or case-insensitive duplicate name fixture', 'Create wallet', 'Rejects max-count and duplicate-name requests.', 'Pass', 'Executable service case'],
  ['UT-WAL-04', 'Wallet', 'WalletService.GetWalletsAsync', 'Owned active/deleted wallets with balances', 'List wallets', 'Excludes soft-deleted wallet, alphabetizes names, and sums active balances.', 'Pass', 'Executable service case'],
  ['UT-WAL-05', 'Wallet', 'WalletService.GetWalletByIdAsync', 'Owned active, deleted, and other-customer wallets', 'Get each wallet ID', 'Returns only owned active wallet; otherwise null.', 'Pass', 'Executable service case'],
  ['UT-WAL-06', 'Wallet', 'WalletService.UpdateWalletAsync', 'Owned wallet and duplicate-name fixture', 'Rename with trim, duplicate name, empty name, then provided type', 'Persists valid rename; rejects duplicate/empty name and any type change.', 'Pass', 'Executable service case'],
  ['UT-WAL-07', 'Wallet', 'WalletService.DeleteWalletAsync', 'Two active wallets plus single-wallet and absent fixtures', 'Delete each target', 'Soft-deletes a nonfinal wallet; throws last_wallet rule for final; absent returns false.', 'Pass', 'Executable service case'],
];

// [ID, Group, Function / boundary, Deferred scenario, Why deferred, Required test layer, Status]
export const deferredCases = [
  ['DF-AUTH-01', 'Auth', 'GoogleLoginCommandHandler / IFirebaseAuthService', 'Google identity declares an unverified email.', 'Provider-contract scenario; current handler only rejects missing email and persists EmailVerified from Firebase.', 'Firebase emulator/contract test', 'Deferred'],
  ['DF-AUTH-02', 'Auth', 'Google login HTTP/error mapping', 'Firebase invalid token maps 401 and Firebase outage maps 503.', 'Requires provider failure translation plus API exception middleware.', 'API + provider contract test', 'Deferred'],
  ['DF-AUTH-03', 'Auth', 'ForgotPassword/Google/Refresh/Logout command validation', 'Blank/malformed request payloads receive validation errors.', 'Missing validators are a code gap; no executable validator exists for all commands.', 'Post-fix unit + API validation test', 'Deferred'],
  ['DF-AUTH-04', 'Auth', 'LoginCommandHandler.Handle', 'Whitespace-padded login email authenticates as normalized email.', 'Current login path has no established trim contract; regression expectation needs approved behavior.', 'Post-fix unit test', 'Deferred'],
  ['DF-AUTH-05', 'Auth', 'RefreshTokenCommandHandler.Handle', 'Two concurrent refreshes of one token yield one success only.', 'Needs transactional/unique concurrency behavior in a real PostgreSQL test.', 'DB concurrency integration test', 'Deferred'],
  ['DF-AUTH-06', 'Auth', 'Token persistence/logging', 'Refresh/reset tokens are stored hashed and never appear in logs.', 'Cross-cutting persistence/log sink inspection, not isolated handler behavior.', 'Security integration/log review', 'Deferred'],
  ['DF-AUTH-07', 'Auth', 'Auth endpoints', 'Register/login/reset are rate-limited and email-send failure leaves consistent token state.', 'Requires middleware plus provider fault behavior and persistence transaction policy.', 'API/provider integration test', 'Deferred'],

  ['DF-PROF-01', 'Profile', 'UpdateProfileCommandHandler.Handle', 'DOB/gender updates accept only defined enum values and can deliberately clear optional values.', 'Null is currently indistinguishable from omitted in command handling; enum/DOB rules need specification.', 'Post-fix unit/API test', 'Deferred'],
  ['DF-PROF-02', 'Profile', 'ProfileController.UploadAvatar', 'Large upload is rejected before full buffering.', 'Controller always copies IFormFile into MemoryStream before handler size validation.', 'API/performance test after design change', 'Deferred'],
  ['DF-PROF-03', 'Profile', 'UploadAvatarCommandHandler.Handle', 'Storage/DB failure does not lose prior avatar or leave orphaned new asset.', 'Requires fault-injection across storage and DB transaction/compensation boundaries.', 'Provider/DB integration test', 'Deferred'],
  ['DF-PROF-04', 'Profile', 'Authenticated access after deactivation', 'Already-issued access JWT cannot access protected profile endpoints after account deactivation.', 'JWT authorization and current-user state check are API-wide behavior.', 'API security integration test', 'Deferred'],

  ['DF-CAT-01', 'Category', 'CategoryService.CreateCategoryAsync / EF mapping', 'IsMandatory supplied at create/update round-trips and persists.', 'DbContext currently ignores IsMandatory mapping; requires schema/mapping validation.', 'Post-fix DB integration test', 'Deferred'],
  ['DF-CAT-02', 'Category', 'CategoryService.UpdateCategoryAsync', 'Type changes preserve required expense/income bucket invariant and prevent invalid existing-state transitions.', 'Current update changes type before validating dependent bucket invariant.', 'Post-fix service test', 'Deferred'],
  ['DF-CAT-03', 'Category', 'CategoryService.UpdateCategoryAsync / DeleteCategoryAsync', 'Reserved savings-goal category cannot be renamed, retyped, or deleted.', 'Only customer bucket reassignment is guarded; global admin protections are incomplete.', 'Post-fix service/API test', 'Deferred'],
  ['DF-CAT-04', 'Category', 'CategoryService', 'Category ID/name/en/icon/color adhere to DB length limits and nullable semantics.', 'Requires defined validation rules and DB schema contract; current service may surface persistence errors.', 'Validation + DB integration test', 'Deferred'],
  ['DF-CAT-05', 'Category', 'CategoryService + BudgetService', 'Global bucket/type changes interact correctly with existing budget seeding/allocation.', 'Cross-service behavior needs a defined business policy and persisted budget fixtures.', 'Business-flow integration test', 'Deferred'],

  ['DF-WAL-01', 'Wallet', 'WalletService.CreateWalletAsync', 'Concurrent same-name or 10th-wallet creates cannot bypass uniqueness/max policy.', 'Check-then-insert/count flow needs database constraint or serializable concurrency coverage.', 'PostgreSQL concurrency integration test', 'Deferred'],
  ['DF-WAL-02', 'Wallet', 'WalletService.CreateWalletAsync', 'Opening balance produces the required auditable opening-balance transaction/history.', 'Current creation assigns balance directly; history policy must be confirmed/implemented.', 'Post-fix DB business-flow test', 'Deferred'],
  ['DF-WAL-03', 'Wallet', 'WalletService.DeleteWalletAsync', 'Delete policy for nonzero balances, transactions, linked accounts, and transfer history is enforced.', 'Current behavior only blocks deleting final active wallet; remaining policy is ambiguous.', 'Approved-policy integration test', 'Deferred'],
];

// [ID, Severity, Area, Confirmed gap, Evidence / impact, Recommended next action]
export const gaps = [
  ['GAP-AUTH-01', 'High', 'Google sign-in', 'Google account with EmailVerified=false can still receive local tokens.', 'Handler checks only Email is nonempty, then records Firebase EmailVerified and issues tokens.', 'Require verified Firebase email before matching/creating or explicitly define an alternate verification flow.'],
  ['GAP-AUTH-02', 'Medium', 'Google sign-in', 'Firebase invalid-token and provider-unavailable failures lack a clear 401 versus 503 contract.', 'Provider exception translation crosses Firebase service, handler, and exception middleware.', 'Map invalid credentials to UnauthorizedException and provider outage to IntegrationUnavailableException; add contract tests.'],
  ['GAP-AUTH-03', 'Medium', 'Auth input validation', 'Forgot-password, Google-login, refresh-token, and logout lack dedicated validators.', 'Malformed/blank input may reach handlers/providers instead of ValidationBehavior.', 'Add FluentValidation validators and unit-test them.'],
  ['GAP-AUTH-04', 'Low', 'Password login', 'Login email has no documented trim normalization.', 'A whitespace-padded valid email may fail lookup while register path normalizes data.', 'Normalize/trim at boundary or handler and add a regression test.'],
  ['GAP-AUTH-05', 'High', 'Token lifecycle', 'Refresh rotation has race/raw-token/log-exposure concerns.', 'Concurrent use, plaintext persistence/logging boundaries require DB and security controls.', 'Use atomic conditional rotation, hash stored secrets, redact logs, and test concurrent refreshes.'],
  ['GAP-AUTH-06', 'Medium', 'Auth abuse/failure policy', 'No verified rate-limit or email-failure persistence policy for register/reset flows.', 'Provider failure can leave ambiguous token/account state; abuse controls are cross-cutting.', 'Add rate limiting and transactional/compensation policy with provider-fault tests.'],
  ['GAP-PROF-01', 'Medium', 'Profile fields', 'DOB/gender validation and intentional null-clear semantics are unresolved.', 'Nullable command fields make omitted and clear indistinguishable; enum values require validation.', 'Define PATCH/clear contract and validate DOB/enum values.'],
  ['GAP-PROF-02', 'Medium', 'Avatar upload', 'Controller buffers full file before size rejection.', 'MemoryStream copy occurs before handler checks the 5MB limit.', 'Enforce request/file limits before buffering or stream with bounded copy.'],
  ['GAP-PROF-03', 'High', 'Avatar replacement', 'Delete-old/upload-new/save order can lose previous image or orphan assets on failure.', 'Old URL is deleted before new upload and DB persistence; no compensation is visible.', 'Upload first, persist atomically where possible, then delete old asset with compensation/cleanup.'],
  ['GAP-PROF-04', 'High', 'Deactivation security', 'Existing access JWT may remain usable after account deactivation.', 'Token revocation covers refresh tokens; access-token state enforcement is cross-cutting.', 'Validate active account/token version on protected requests or shorten/revoke access tokens.'],
  ['GAP-CAT-01', 'High', 'Category persistence', 'IsMandatory is accepted but EF model ignores the property.', 'FinVietDbContext ignores IsMandatory even though DTO/service map it.', 'Map/add column consistently and prove round-trip with DB integration coverage.'],
  ['GAP-CAT-02', 'High', 'Category update', 'Type updates can violate income/expense default-bucket invariants.', 'Update changes type without revalidating/clearing dependent DefaultBucket.', 'Validate the final aggregate state before save.'],
  ['GAP-CAT-03', 'Medium', 'Savings-goal category', 'Global admin update/delete safeguards for reserved savings-goal category are incomplete.', 'Bucket override is protected, but update/delete paths do not reserve it.', 'Prevent incompatible edits/deletion or define controlled migration behavior.'],
  ['GAP-CAT-04', 'Medium', 'Category validation', 'Length and null handling can fall through to persistence errors.', 'Service lacks comprehensive DTO/FluentValidation aligned to schema limits.', 'Add boundary validation and consistent null/empty semantics.'],
  ['GAP-CAT-05', 'Medium', 'Budgets', 'Global category/bucket changes have unspecified budget-seeding interaction.', 'Categories and BudgetService can affect allocation semantics across services.', 'Specify policy and create end-to-end business-flow tests.'],
  ['GAP-WAL-01', 'High', 'Wallet creation', 'Duplicate-name and maximum-wallet checks race under concurrent creates.', 'Both rely on read-before-write checks without demonstrated DB constraint/serializable handling.', 'Add unique constraint/atomic rule enforcement and PostgreSQL concurrency tests.'],
  ['GAP-WAL-02', 'Medium', 'Opening balance', 'Initial balance changes wallet balance without auditable opening history.', 'CreateWalletAsync sets Balance directly and creates no transaction.', 'Define and implement opening-balance transaction/history policy.'],
  ['GAP-WAL-03', 'Medium', 'Wallet deletion', 'Deletion policy for balances/history/linked resources is ambiguous.', 'Current implementation only blocks deleting the last active wallet then soft-deletes.', 'Specify invariants and enforce them before deletion.'],
];

export const counts = () => ({
  functionGroups: new Set(functions.map(row => row[0])).size,
  functions: functions.length,
  executable: cases.length,
  passed: cases.filter(row => row[6] === 'Pass').length,
  deferred: deferredCases.length,
  gaps: gaps.length,
  total: cases.length + deferredCases.length,
});
