using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinViet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin",
                columns: table => new
                {
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("admin_pkey", x => x.admin_id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                columns: table => new
                {
                    category_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    category_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    expense_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    model_bucket = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("category_pkey", x => x.category_id);
                });

            migrationBuilder.CreateTable(
                name: "financial_model",
                columns: table => new
                {
                    model_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    model_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    allocation_rules = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("financial_model_pkey", x => x.model_id);
                });

            migrationBuilder.CreateTable(
                name: "scoring_criteria",
                columns: table => new
                {
                    criterion_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    criterion_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    formula = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("scoring_criteria_pkey", x => x.criterion_id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plan",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    features_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("subscription_plan_pkey", x => x.plan_id);
                });

            migrationBuilder.CreateTable(
                name: "customer",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("customer_pkey", x => x.customer_id);
                    table.ForeignKey(
                        name: "customer_admin_id_fkey",
                        column: x => x.admin_id,
                        principalTable: "admin",
                        principalColumn: "admin_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "system_analytics",
                columns: table => new
                {
                    analytics_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metric_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metric_value = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("system_analytics_pkey", x => x.analytics_id);
                    table.ForeignKey(
                        name: "system_analytics_admin_id_fkey",
                        column: x => x.admin_id,
                        principalTable: "admin",
                        principalColumn: "admin_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "model_allocation",
                columns: table => new
                {
                    allocation_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("model_allocation_pkey", x => x.allocation_id);
                    table.ForeignKey(
                        name: "model_allocation_model_id_fkey",
                        column: x => x.model_id,
                        principalTable: "financial_model",
                        principalColumn: "model_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_report",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generated_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    content = table.Column<string>(type: "text", nullable: true),
                    spending_score = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ai_report_pkey", x => x.report_id);
                    table.ForeignKey(
                        name: "ai_report_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "budget_plan",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("budget_plan_pkey", x => x.plan_id);
                    table.ForeignKey(
                        name: "budget_plan_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "budget_plan_model_id_fkey",
                        column: x => x.model_id,
                        principalTable: "financial_model",
                        principalColumn: "model_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "customer_subscription",
                columns: table => new
                {
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'ACTIVE'::character varying")
                },
                constraints: table =>
                {
                    table.PrimaryKey("customer_subscription_pkey", x => x.subscription_id);
                    table.ForeignKey(
                        name: "customer_subscription_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "customer_subscription_plan_id_fkey",
                        column: x => x.plan_id,
                        principalTable: "subscription_plan",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "income_source",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("income_source_pkey", x => x.source_id);
                    table.ForeignKey(
                        name: "income_source_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saving_goal",
                columns: table => new
                {
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    goal_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    current_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0.00"),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("saving_goal_pkey", x => x.goal_id);
                    table.ForeignKey(
                        name: "saving_goal_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallet",
                columns: table => new
                {
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wallet_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    wallet_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0.00")
                },
                constraints: table =>
                {
                    table.PrimaryKey("wallet_pkey", x => x.wallet_id);
                    table.ForeignKey(
                        name: "wallet_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_report_detail",
                columns: table => new
                {
                    detail_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criterion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ai_report_detail_pkey", x => x.detail_id);
                    table.ForeignKey(
                        name: "ai_report_detail_criterion_id_fkey",
                        column: x => x.criterion_id,
                        principalTable: "scoring_criteria",
                        principalColumn: "criterion_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "ai_report_detail_report_id_fkey",
                        column: x => x.report_id,
                        principalTable: "ai_report",
                        principalColumn: "report_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_message",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sender_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    timestamps = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("chat_message_pkey", x => x.message_id);
                    table.ForeignKey(
                        name: "chat_message_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "chat_message_report_id_fkey",
                        column: x => x.report_id,
                        principalTable: "ai_report",
                        principalColumn: "report_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "category_budget",
                columns: table => new
                {
                    category_budget_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_limit = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    current_spent = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0.00"),
                    threshold_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    threshold_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("category_budget_pkey", x => x.category_budget_id);
                    table.ForeignKey(
                        name: "category_budget_category_id_fkey",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "category_budget_plan_id_fkey",
                        column: x => x.plan_id,
                        principalTable: "budget_plan",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saving_goal_contribution",
                columns: table => new
                {
                    contribution_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    contribution_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("saving_goal_contribution_pkey", x => x.contribution_id);
                    table.ForeignKey(
                        name: "saving_goal_contribution_goal_id_fkey",
                        column: x => x.goal_id,
                        principalTable: "saving_goal",
                        principalColumn: "goal_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_batch",
                columns: table => new
                {
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    import_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("import_batch_pkey", x => x.batch_id);
                    table.ForeignKey(
                        name: "import_batch_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "import_batch_wallet_id_fkey",
                        column: x => x.wallet_id,
                        principalTable: "wallet",
                        principalColumn: "wallet_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_budget_id = table.Column<Guid>(type: "uuid", nullable: true),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("notification_pkey", x => x.notification_id);
                    table.ForeignKey(
                        name: "notification_category_budget_id_fkey",
                        column: x => x.category_budget_id,
                        principalTable: "category_budget",
                        principalColumn: "category_budget_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "notification_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "notification_goal_id_fkey",
                        column: x => x.goal_id,
                        principalTable: "saving_goal",
                        principalColumn: "goal_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "transaction",
                columns: table => new
                {
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    transaction_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("transaction_pkey", x => x.transaction_id);
                    table.ForeignKey(
                        name: "transaction_batch_id_fkey",
                        column: x => x.batch_id,
                        principalTable: "import_batch",
                        principalColumn: "batch_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "transaction_category_id_fkey",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "transaction_report_id_fkey",
                        column: x => x.report_id,
                        principalTable: "ai_report",
                        principalColumn: "report_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "transaction_source_id_fkey",
                        column: x => x.source_id,
                        principalTable: "income_source",
                        principalColumn: "source_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "transaction_wallet_id_fkey",
                        column: x => x.wallet_id,
                        principalTable: "wallet",
                        principalColumn: "wallet_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category_correction_log",
                columns: table => new
                {
                    log_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    corrected_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_ai_guess = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("category_correction_log_pkey", x => x.log_id);
                    table.ForeignKey(
                        name: "category_correction_log_admin_id_fkey",
                        column: x => x.admin_id,
                        principalTable: "admin",
                        principalColumn: "admin_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "category_correction_log_corrected_category_id_fkey",
                        column: x => x.corrected_category_id,
                        principalTable: "category",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "category_correction_log_customer_id_fkey",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "category_correction_log_transaction_id_fkey",
                        column: x => x.transaction_id,
                        principalTable: "transaction",
                        principalColumn: "transaction_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "admin_email_key",
                table: "admin",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "admin_username_key",
                table: "admin",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_report_customer_id",
                table: "ai_report",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_report_detail_criterion_id",
                table: "ai_report_detail",
                column: "criterion_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_report_detail_report_id",
                table: "ai_report_detail",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_plan_customer_id",
                table: "budget_plan",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_plan_model_id",
                table: "budget_plan",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_category_budget_category_id",
                table: "category_budget",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_category_budget_plan_id",
                table: "category_budget",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_category_correction_log_admin_id",
                table: "category_correction_log",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_category_correction_log_corrected_category_id",
                table: "category_correction_log",
                column: "corrected_category_id");

            migrationBuilder.CreateIndex(
                name: "IX_category_correction_log_customer_id",
                table: "category_correction_log",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_category_correction_log_transaction_id",
                table: "category_correction_log",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_customer_id",
                table: "chat_message",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_report_id",
                table: "chat_message",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "customer_email_key",
                table: "customer",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_admin_id",
                table: "customer",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_subscription_customer_id",
                table: "customer_subscription",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_subscription_plan_id",
                table: "customer_subscription",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_batch_customer_id",
                table: "import_batch",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_batch_wallet_id",
                table: "import_batch",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "IX_income_source_customer_id",
                table: "income_source",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_model_allocation_model_id",
                table: "model_allocation",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_category_budget_id",
                table: "notification",
                column: "category_budget_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_customer_id",
                table: "notification",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_goal_id",
                table: "notification",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "IX_saving_goal_customer_id",
                table: "saving_goal",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_saving_goal_contribution_goal_id",
                table: "saving_goal_contribution",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "IX_system_analytics_admin_id",
                table: "system_analytics",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_batch_id",
                table: "transaction",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_category_id",
                table: "transaction",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_report_id",
                table: "transaction",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_source_id",
                table: "transaction",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_wallet_id",
                table: "transaction",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_customer_id",
                table: "wallet",
                column: "customer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_report_detail");

            migrationBuilder.DropTable(
                name: "category_correction_log");

            migrationBuilder.DropTable(
                name: "chat_message");

            migrationBuilder.DropTable(
                name: "customer_subscription");

            migrationBuilder.DropTable(
                name: "model_allocation");

            migrationBuilder.DropTable(
                name: "notification");

            migrationBuilder.DropTable(
                name: "saving_goal_contribution");

            migrationBuilder.DropTable(
                name: "system_analytics");

            migrationBuilder.DropTable(
                name: "scoring_criteria");

            migrationBuilder.DropTable(
                name: "transaction");

            migrationBuilder.DropTable(
                name: "subscription_plan");

            migrationBuilder.DropTable(
                name: "category_budget");

            migrationBuilder.DropTable(
                name: "saving_goal");

            migrationBuilder.DropTable(
                name: "import_batch");

            migrationBuilder.DropTable(
                name: "ai_report");

            migrationBuilder.DropTable(
                name: "income_source");

            migrationBuilder.DropTable(
                name: "category");

            migrationBuilder.DropTable(
                name: "budget_plan");

            migrationBuilder.DropTable(
                name: "wallet");

            migrationBuilder.DropTable(
                name: "financial_model");

            migrationBuilder.DropTable(
                name: "customer");

            migrationBuilder.DropTable(
                name: "admin");
        }
    }
}
