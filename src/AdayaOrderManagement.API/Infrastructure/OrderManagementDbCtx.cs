using AdayaOrderManagement.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AdayaOrderManagement.API.Infrastructure;

public sealed class OrderManagementDbCtx(DbContextOptions<OrderManagementDbCtx> options) : DbContext(options)
{
  public DbSet<Product> Products { get; set; } = null!;

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Product>(entity =>
    {
      entity.Property(p => p.Name).HasMaxLength(200).IsRequired();

      entity.Property(p => p.Price).HasPrecision(18, 2);

      // ini tuh safety di level db constraint : jadi ketika ada proses write < 0, db bakal cancel/return error
      entity.ToTable(table => table.HasCheckConstraint("CK_Products_Stock", "[StockQuantity] >= 0"));
    });
  }
}