using AdayaOrderManagement.API.Infrastructure;
using AdayaOrderManagement.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AdayaOrderManagement.API.Application.Orders;

public enum UpdateOrderStatusOutcome
{
  Updated,
  NotFound,
  InvalidTransition,
  ConcurrencyConflict,
  StockRestoreFailed
}

public sealed record UpdateOrderStatusResult(
  UpdateOrderStatusOutcome Outcome,
  CreateOrderResponse? Order = null);

public sealed class OrderStatusService(OrderManagementDbCtx db)
{
  public async Task<UpdateOrderStatusResult> UpdateAsync(
    Guid orderId, OrderStatus targetStatus, CancellationToken cancellationToken)
  {
    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

    // order ini sengaja di-track supaya ef bisa ngebandingin rowversion waktu disimpan
    var order = await db.Orders.Include(x => x.Items)
      .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

    if (order is null)
    {
      await transaction.RollbackAsync(cancellationToken);
      return new(UpdateOrderStatusOutcome.NotFound);
    }

    if (!IsValidTransition(order.Status, targetStatus))
    {
      await transaction.RollbackAsync(cancellationToken);
      return new(UpdateOrderStatusOutcome.InvalidTransition);
    }

    order.Status = targetStatus;
    order.UpdatedAtUtc = DateTime.UtcNow;

    try
    {
      // kalau request lain udah ngubah order ini, save bakal kena konflik rowversion
      await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
      await transaction.RollbackAsync(cancellationToken);
      return new(UpdateOrderStatusOutcome.ConcurrencyConflict);
    }

    if (targetStatus == OrderStatus.CANCELLED)
    {
      foreach (var item in order.Items.OrderBy(x => x.ProductId))
      {
        // tambah stok langsung di database biar aman kalau ada transaksi lain yang ikut ngubah stok
        var updated = await db.Products
          .Where(x => x.Id == item.ProductId && x.StockQuantity <= int.MaxValue - item.OrderQuantity)
          .ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.StockQuantity, x => x.StockQuantity + item.OrderQuantity),
            cancellationToken);

        if (updated != 1)
        {
          // rollback juga ngebatalin perubahan status dan stok item yang sempat dipulihkan
          await transaction.RollbackAsync(cancellationToken);
          return new(UpdateOrderStatusOutcome.StockRestoreFailed);
        }
      }
    }

    await transaction.CommitAsync(cancellationToken);
    return new(UpdateOrderStatusOutcome.Updated, CreateOrderResponse.FromOrder(order));
  }

  public static bool IsValidTransition(OrderStatus current, OrderStatus target) =>
    (current, target) switch
    {
      (OrderStatus.PENDING, OrderStatus.CONFIRMED or OrderStatus.CANCELLED) => true,
      (OrderStatus.CONFIRMED, OrderStatus.SHIPPED or OrderStatus.CANCELLED) => true,
      (OrderStatus.SHIPPED, OrderStatus.DELIVERED) => true,
      _ => false
    };
}
