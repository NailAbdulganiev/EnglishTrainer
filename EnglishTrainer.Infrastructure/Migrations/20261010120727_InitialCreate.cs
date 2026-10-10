using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EnglishTrainer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "decks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "UTC"),
                    telegram_id = table.Column<long>(type: "bigint", nullable: true),
                    telegram_linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "words",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    lemma = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    part_of_speech = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ipa_uk = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    ipa_us = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    frequency_rank = table.Column<int>(type: "integer", nullable: true),
                    level = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_words", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "daily_stats",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reviewed_count = table.Column<int>(type: "integer", nullable: false),
                    new_words_started = table.Column<int>(type: "integer", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    learned_total = table.Column<int>(type: "integer", nullable: false),
                    study_seconds = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_stats", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_stats_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_sessions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    deck_id = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_review_count = table.Column<int>(type: "integer", nullable: false),
                    planned_new_count = table.Column<int>(type: "integer", nullable: false),
                    answered_count = table.Column<int>(type: "integer", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_study_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_study_sessions_decks_deck_id",
                        column: x => x.deck_id,
                        principalTable: "decks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_study_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    new_words_per_day = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    max_cards_per_session = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    daily_goal = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    daily_reminder_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    reminder_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    show_transcription_upfront = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_states",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    phase = table.Column<int>(type: "integer", nullable: false),
                    stability = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    difficulty = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    interval_days = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reps = table.Column<int>(type: "integer", nullable: false),
                    lapses = table.Column<int>(type: "integer", nullable: false),
                    last_reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_states", x => x.id);
                    table.ForeignKey(
                        name: "FK_card_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_card_states_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deck_words",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    deck_id = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deck_words", x => x.id);
                    table.ForeignKey(
                        name: "FK_deck_words_decks_deck_id",
                        column: x => x.deck_id,
                        principalTable: "decks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_deck_words_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "word_examples",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    text_en = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    text_ru = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    audio_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    attribution = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_word_examples", x => x.id);
                    table.ForeignKey(
                        name: "FK_word_examples_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "word_translations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    language_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    text = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sense = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_word_translations", x => x.id);
                    table.ForeignKey(
                        name: "FK_word_translations_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_state_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    elapsed_days = table.Column<double>(type: "double precision", nullable: false),
                    scheduled_days = table.Column<double>(type: "double precision", nullable: false),
                    phase_before = table.Column<int>(type: "integer", nullable: false),
                    stability_before = table.Column<double>(type: "double precision", nullable: false),
                    difficulty_before = table.Column<double>(type: "double precision", nullable: false),
                    elapsed_ms = table.Column<int>(type: "integer", nullable: true),
                    exercise = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_review_logs_card_states_card_state_id",
                        column: x => x.card_state_id,
                        principalTable: "card_states",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_session_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    study_session_id = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<int>(type: "integer", nullable: false),
                    card_state_id = table.Column<int>(type: "integer", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    exercise = table.Column<int>(type: "integer", nullable: false),
                    is_answered = table.Column<bool>(type: "boolean", nullable: false),
                    answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    is_requeued = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_study_session_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_study_session_items_card_states_card_state_id",
                        column: x => x.card_state_id,
                        principalTable: "card_states",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_study_session_items_study_sessions_study_session_id",
                        column: x => x.study_session_id,
                        principalTable: "study_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_study_session_items_words_word_id",
                        column: x => x.word_id,
                        principalTable: "words",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_card_states_user_id_due_at",
                table: "card_states",
                columns: new[] { "user_id", "due_at" });

            migrationBuilder.CreateIndex(
                name: "IX_card_states_user_id_word_id",
                table: "card_states",
                columns: new[] { "user_id", "word_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_card_states_word_id",
                table: "card_states",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "IX_daily_stats_user_id_date",
                table: "daily_stats",
                columns: new[] { "user_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deck_words_deck_id_word_id",
                table: "deck_words",
                columns: new[] { "deck_id", "word_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deck_words_word_id",
                table: "deck_words",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "IX_decks_slug",
                table: "decks",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_review_logs_card_state_id_reviewed_at",
                table: "review_logs",
                columns: new[] { "card_state_id", "reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_review_logs_user_id_reviewed_at",
                table: "review_logs",
                columns: new[] { "user_id", "reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_study_session_items_card_state_id",
                table: "study_session_items",
                column: "card_state_id");

            migrationBuilder.CreateIndex(
                name: "IX_study_session_items_study_session_id_position",
                table: "study_session_items",
                columns: new[] { "study_session_id", "position" });

            migrationBuilder.CreateIndex(
                name: "IX_study_session_items_word_id",
                table: "study_session_items",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_sessions_active_per_user",
                table: "study_sessions",
                column: "user_id",
                unique: true,
                filter: "\"status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_study_sessions_deck_id",
                table: "study_sessions",
                column: "deck_id");

            migrationBuilder.CreateIndex(
                name: "IX_study_sessions_user_id_local_date",
                table: "study_sessions",
                columns: new[] { "user_id", "local_date" });

            migrationBuilder.CreateIndex(
                name: "IX_user_settings_user_id",
                table: "user_settings",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_public_id",
                table: "users",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_telegram_id",
                table: "users",
                column: "telegram_id",
                unique: true,
                filter: "\"telegram_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_word_examples_word_id",
                table: "word_examples",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "IX_word_translations_word_id_language_code",
                table: "word_translations",
                columns: new[] { "word_id", "language_code" });

            migrationBuilder.CreateIndex(
                name: "IX_words_frequency_rank",
                table: "words",
                column: "frequency_rank");

            migrationBuilder.CreateIndex(
                name: "IX_words_lemma_part_of_speech",
                table: "words",
                columns: new[] { "lemma", "part_of_speech" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_stats");

            migrationBuilder.DropTable(
                name: "deck_words");

            migrationBuilder.DropTable(
                name: "review_logs");

            migrationBuilder.DropTable(
                name: "study_session_items");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "word_examples");

            migrationBuilder.DropTable(
                name: "word_translations");

            migrationBuilder.DropTable(
                name: "card_states");

            migrationBuilder.DropTable(
                name: "study_sessions");

            migrationBuilder.DropTable(
                name: "words");

            migrationBuilder.DropTable(
                name: "decks");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
