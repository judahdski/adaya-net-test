using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdayaOrderManagement.API.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUppercaseOrderStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Status_Order",
                table: "Orders");

            migrationBuilder.Sql(
                "UPDATE [Orders] SET [Status] = UPPER([Status]) WHERE [Status] COLLATE Latin1_General_100_BIN2 <> UPPER([Status]) COLLATE Latin1_General_100_BIN2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Status_Order",
                table: "Orders",
                sql: "[Status] COLLATE Latin1_General_100_BIN2 IN ('PENDING', 'CONFIRMED', 'SHIPPED', 'DELIVERED', 'CANCELLED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Status_Order",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Status_Order",
                table: "Orders",
                sql: "[Status] IN ('PENDING', 'CONFIRMED', 'SHIPPED', 'DELIVERED', 'CANCELLED')");
        }
    }
}
