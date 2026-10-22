using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdayaOrderManagement.API.Migrations
{
    /// <inheritdoc />
    public partial class FixIdempotencyHashAndOrderLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PayloadHash",
                table: "IdempotencyRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1)");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRequests_OrderId",
                table: "IdempotencyRequests",
                column: "OrderId",
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_IdempotencyRequests_Orders_OrderId",
                table: "IdempotencyRequests",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IdempotencyRequests_Orders_OrderId",
                table: "IdempotencyRequests");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRequests_OrderId",
                table: "IdempotencyRequests");

            migrationBuilder.AlterColumn<string>(
                name: "PayloadHash",
                table: "IdempotencyRequests",
                type: "nvarchar(1)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);
        }
    }
}
