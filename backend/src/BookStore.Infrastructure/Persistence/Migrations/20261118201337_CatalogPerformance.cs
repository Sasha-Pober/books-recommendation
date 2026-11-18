using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<int>(
                name: "rating_count",
                table: "books",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rating_sum",
                table: "books",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Hand-written: fill the new counters from the existing ratings (e.g. the imported Goodreads ones).
            migrationBuilder.Sql("""
                UPDATE books b SET rating_count = r.count, rating_sum = r.sum
                FROM (SELECT book_id, count(*) AS count, sum(score) AS sum FROM ratings GROUP BY book_id) r
                WHERE b.id = r.book_id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_books_title_trgm",
                table: "books",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_books_ratings",
                table: "books",
                sql: "rating_count >= 0 AND rating_sum >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_authors_full_name_trgm",
                table: "authors",
                column: "full_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_books_title_trgm",
                table: "books");

            migrationBuilder.DropCheckConstraint(
                name: "ck_books_ratings",
                table: "books");

            migrationBuilder.DropIndex(
                name: "ix_authors_full_name_trgm",
                table: "authors");

            migrationBuilder.DropColumn(
                name: "rating_count",
                table: "books");

            migrationBuilder.DropColumn(
                name: "rating_sum",
                table: "books");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
