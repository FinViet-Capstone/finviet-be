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
    public virtual DbSet<CategoryRequest> CategoryRequests { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // ── Customer: new auth columns ───────────────────────────
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(e => e.AvatarUrl)
                .HasMaxLength(500)
                .HasColumnName("avatar_url");

            entity.Property(e => e.MonthlyIncomeExpected)
                .HasPrecision(15, 2)
                .HasColumnName("monthly_income_expected");

            entity.Property(e => e.GoogleId)
                .HasMaxLength(255)
                .HasColumnName("google_id");

            entity.Property(e => e.IsEmailVerified)
                .HasDefaultValue(false)
                .HasColumnName("is_email_verified");

            entity.Property(e => e.EmailVerifiedAt)
                .HasColumnName("email_verified_at");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");

            entity.Property(e => e.DeletedAt)
                .HasColumnName("deleted_at");
        });

        // ── RefreshToken entity ───────────────────────────────────
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("refresh_token_pkey");
            entity.ToTable("refresh_token");

            entity.HasAlternateKey(e => e.Token).HasName("refresh_token_token_key");
            entity.HasIndex(e => e.CustomerId, "idx_refresh_token_customer_id");

            entity.Property(e => e.TokenId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("token_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Token).HasMaxLength(512).HasColumnName("token");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.IsRevoked).HasDefaultValue(false).HasColumnName("is_revoked");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Customer)
                .WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("refresh_token_customer_id_fkey");
        });

        // ── EmailVerificationToken entity ────────────────────────
        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(e => e.TokenId).HasName("email_verification_token_pkey");
            entity.ToTable("email_verification_token");

            entity.HasAlternateKey(e => e.Token).HasName("email_verification_token_token_key");
            entity.HasIndex(e => e.CustomerId, "idx_email_verification_customer_id");

            entity.Property(e => e.TokenId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("token_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Token).HasMaxLength(512).HasColumnName("token");
            entity.Property(e => e.TokenType).HasMaxLength(50).HasColumnName("token_type");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.UsedAt).HasColumnName("used_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Customer)
                .WithMany(p => p.EmailVerificationTokens)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("email_verification_token_customer_id_fkey");
        });

        // ── CategoryRequest entity ───────────────────────────────
        modelBuilder.Entity<CategoryRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId).HasName("category_request_pkey");
            entity.ToTable("category_request");

            entity.Property(e => e.RequestId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("request_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CategoryName).HasMaxLength(100).HasColumnName("category_name");
            entity.Property(e => e.Type).HasMaxLength(50).HasColumnName("type");
            entity.Property(e => e.ExpenseClass).HasMaxLength(50).HasColumnName("expense_class");
            entity.Property(e => e.Note).HasMaxLength(500).HasColumnName("note");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");
            entity.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(e => e.ReviewNote).HasMaxLength(500).HasColumnName("review_note");
            entity.Property(e => e.CreatedCategoryId).HasMaxLength(40).HasColumnName("created_category_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");

            entity.HasOne(d => d.Customer)
                .WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("category_request_customer_id_fkey");
        });
    }
}
