using AdayaOrderManagement.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AdayaOrderManagement.API.Infrastructure;

public sealed class OrderManagementDbCtx(DbContextOptions<OrderManagementDbCtx> options) : DbContext(options)
{
  public DbSet<Product> Products { get; set; } = null!;
  public DbSet<Order> Orders { get; set; } = null!;
  public DbSet<OrderItem> OrderItems { get; set; } = null!;
  public DbSet<IdempotencyRequest> IdempotencyRequests { get; set; } = null!;

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    // model : Product
    modelBuilder.Entity<Product>(entity =>
    {
      entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
      entity.Property(p => p.Price).HasPrecision(18, 2);

      // ini tuh safety di level db constraint : jadi ketika ada proses write < 0, db bakal cancel/return error
      entity.ToTable(table => table.HasCheckConstraint("CK_Products_Stock", "[StockQuantity] >= 0"));
    });

    // model : OrderItem
    modelBuilder.Entity<OrderItem>(entity =>
    {
      // konsepnya sama kayak check constraint stok di tabel produk
      entity.ToTable(table => table.HasCheckConstraint("CK_Order_Quantity", "[OrderQuantity] > 0"));

      entity.HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique();

      entity.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId);

      entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);

      entity.Property(x => x.UnitPrice).HasPrecision(18, 2);

      // aturan penghapusan:
      // kalo product udah ada di table 'OrderItem', product itu 'gaboleh' dihapus karena di-restrict
      entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
      // kalo order (parent) diapus, itemsnya (child) ngikut kehapus (cascading)
      entity.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    });

    // model : Order
    modelBuilder.Entity<Order>(entity =>
    {
      entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

      entity.ToTable(x => x.HasCheckConstraint("CK_Status_Order", "[Status] COLLATE Latin1_General_100_BIN2 IN ('PENDING', 'CONFIRMED', 'SHIPPED', 'DELIVERED', 'CANCELLED')"));

      entity.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });

      entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });

      entity.Property(x => x.RowVersion).IsRowVersion(); // nandain sebagai row-version / concurrent token yang bakalan dipake sama EF Core ketika ada conflict perubahan data (Optimistic Concurrent)
    });

    // model : IdempotencyRequest
    modelBuilder.Entity<IdempotencyRequest>(entity =>
    {
      entity.HasKey(x => x.Key); //ngebuat jadi PK

      entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
      entity.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
      entity.HasOne(x => x.Order).WithOne().HasForeignKey<IdempotencyRequest>(x => x.OrderId)
        .OnDelete(DeleteBehavior.Restrict);
    });
  }
}

/*
  fungsi EF Core:
  -native ORM-nya dotnet, mapping rules
  -ngatur config db di API
  -setting rules yang relevan dengan db (delete behaviour, required)
*/
