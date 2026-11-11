using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BookStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-written instead of InsertData: databases created before this migration already have
            // these roles (with other ids) from the development seeder, so insert only the missing ones.
            migrationBuilder.Sql("""
                INSERT INTO roles (id, name, normalized_name, concurrency_stamp) VALUES
                    ('01990000-0000-7000-8000-00000000c001', 'Customer', 'CUSTOMER', '01990000-0000-7000-8000-00000000c001'),
                    ('01990000-0000-7000-8000-00000000c002', 'Manager', 'MANAGER', '01990000-0000-7000-8000-00000000c002'),
                    ('01990000-0000-7000-8000-00000000c003', 'Admin', 'ADMIN', '01990000-0000-7000-8000-00000000c003')
                ON CONFLICT (normalized_name) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("01990000-0000-7000-8000-00000000c001"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("01990000-0000-7000-8000-00000000c002"));

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("01990000-0000-7000-8000-00000000c003"));
        }
    }
}
