using FinViet.Domain.Enums;
using FinViet.Infrastructure.Persistence.Conventions;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Persistence.Context;

/// <summary>
/// Partial class extending the scaffolded DbContext with auth-related entities.
/// </summary>
public partial class FinVietDbContext
{
    // ── Auth entity DbSets ────────────────────────────────────
    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
    public virtual DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }
    public virtual DbSet<CustomerSetting> CustomerSettings { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // Register Postgres enums so EF can translate them (CLR enums mapped via Npgsql data source).
        modelBuilder.HasPostgresEnum<EmailTokenType>("public", "email_token_type");
        modelBuilder.HasPostgresEnum<Gender>("public", "gender");
        modelBuilder.HasPostgresEnum<AppLanguage>("public", "app_language");
        modelBuilder.HasPostgresEnum<AppTheme>("public", "app_theme");
        modelBuilder.HasPostgresEnum<WalletType>("public", "wallet_type");
        modelBuilder.HasPostgresEnum<TransactionType>("public", "transaction_type");
        modelBuilder.HasPostgresEnum<EntryMethod>("public", "entry_method");
        modelBuilder.HasPostgresEnum<CategoryType>("public", "category_type");
        modelBuilder.HasPostgresEnum<CategorySource>("public", "category_source");
        modelBuilder.HasPostgresEnum<NotificationType>("public", "notification_type");
        modelBuilder.HasPostgresEnum<NotificationEntityType>("public", "notification_entity_type");
        modelBuilder.HasPostgresEnum<SubscriptionStatus>("public", "subscription_status");

        modelBuilder.Entity<Bucket>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("buckets_pkey");
            entity.ToTable("buckets");

            entity.Property(e => e.Id).HasMaxLength(20).HasColumnName("id");
            entity.Property(e => e.NameVi).HasMaxLength(40).HasColumnName("name_vi");
            entity.Property(e => e.NameEn).HasMaxLength(40).HasColumnName("name_en");
            entity.Property(e => e.Color).HasMaxLength(7).HasColumnName("color");
            entity.Property(e => e.Icon).HasMaxLength(60).HasColumnName("icon");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsLocked).HasDefaultValue(false).HasColumnName("is_locked");
        });

        modelBuilder.Entity<SepayLink>(entity =>
        {
            entity.HasKey(e => e.WalletId).HasName("sepay_links_pkey");
            entity.ToTable("sepay_links");

            entity.Property(e => e.WalletId).HasColumnName("wallet_id");
            entity.Property(e => e.AuthMode).HasColumnType("text").HasColumnName("auth_mode").HasDefaultValue("oauth");
            entity.Property(e => e.SepayUserId).HasColumnType("text").HasColumnName("sepay_user_id");
            entity.Property(e => e.SepayBankAccountId).HasColumnName("sepay_bank_account_id");
            entity.Property(e => e.AccountNumber).HasColumnType("text").HasColumnName("account_number");
            entity.Property(e => e.AccountHolderName).HasColumnType("text").HasColumnName("account_holder_name");
            entity.Property(e => e.BankShortName).HasColumnType("text").HasColumnName("bank_short_name");
            entity.Property(e => e.AccessTokenProtected).HasColumnType("text").HasColumnName("access_token");
            entity.Property(e => e.RefreshTokenProtected).HasColumnType("text").HasColumnName("refresh_token");
            entity.Property(e => e.AccessTokenExpiresAt).HasColumnName("access_token_expires_at");
            entity.Property(e => e.LastSyncedAt).HasColumnName("last_synced_at");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").HasColumnName("updated_at");
        });

        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(e => e.BudgetId).HasName("budgets_pkey");
            entity.ToTable("budgets");

            entity.HasIndex(e => new { e.CustomerId, e.CategoryId, e.WalletId }, "idx_budget_customer_category_wallet");

            entity.Property(e => e.BudgetId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CategoryId).HasMaxLength(40).HasColumnName("category_id");
            entity.Property(e => e.WalletId).HasColumnName("wallet_id");
            entity.Property(e => e.MonthlyLimit)
                .HasPrecision(15, 2)
                .HasColumnName("monthly_limit");
            entity.Property(e => e.LastAlertThreshold)
                .HasDefaultValue(0m)
                .HasColumnName("last_alert_threshold");
            entity.Property(e => e.LastAlertMonth).HasMaxLength(7).HasColumnName("last_alert_month");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Customer)
                .WithMany(p => p.Budgets)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("budgets_customer_id_fkey");

            entity.HasOne(d => d.Category)
                .WithMany(p => p.Budgets)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("budgets_category_id_fkey");

            entity.HasOne(d => d.Wallet)
                .WithMany(p => p.Budgets)
                .HasForeignKey(d => d.WalletId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("budgets_wallet_id_fkey");
        });

        modelBuilder.Entity<CustomerCategory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("customer_categories_pkey");
            entity.ToTable("customer_categories");

            entity.HasIndex(e => new { e.CustomerId, e.CategoryId }, "uq_customer_categories_customer_category")
                .IsUnique();
            entity.HasIndex(e => e.CustomerId, "idx_customer_categories_customer_active")
                .HasFilter("is_active = true");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CategoryId).HasMaxLength(40).HasColumnName("category_id");
            entity.Property(e => e.BucketId).HasMaxLength(20).HasColumnName("bucket_id");
            entity.Property(e => e.Source)
                .HasDefaultValue("system")
                .HasColumnName("source")
                .HasConversion(PgEnumStringConverter.Create<CategorySource>());
            entity.Property(e => e.IsActive).HasDefaultValue(true).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Customer)
                .WithMany(p => p.CustomerCategories)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("customer_categories_customer_id_fkey");

            entity.HasOne(d => d.Category)
                .WithMany(p => p.CustomerCategories)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("customer_categories_category_id_fkey");
        });

        // ── RefreshToken entity ───────────────────────────────────
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("refresh_tokens_pkey");
            entity.ToTable("refresh_tokens");

            entity.HasAlternateKey(e => e.Token).HasName("refresh_tokens_token_key");
            entity.HasIndex(e => e.CustomerId, "idx_refresh_tokens_customer_id");

            entity.Property(e => e.TokenId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Token).HasMaxLength(512).HasColumnName("token");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.IsRevoked).HasDefaultValue(false).HasColumnName("is_revoked");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Customer)
                .WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("refresh_tokens_customer_id_fkey");
        });

        // ── EmailVerificationToken entity ────────────────────────
        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("email_verification_tokens_pkey");
            entity.ToTable("email_verification_tokens");

            entity.HasAlternateKey(e => e.Token).HasName("email_verification_tokens_token_key");
            entity.HasIndex(e => e.CustomerId, "idx_email_verification_tokens_customer_id");

            entity.Property(e => e.TokenId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Token).HasMaxLength(512).HasColumnName("token");
            entity.Property(e => e.TokenType).HasColumnName("token_type");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.UsedAt).HasColumnName("used_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Customer)
                .WithMany(p => p.EmailVerificationTokens)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("email_verification_tokens_customer_id_fkey");
        });

    }
}
