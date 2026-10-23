using AdayaOrderManagement.API.Infrastructure;
using AdayaOrderManagement.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AdayaOrderManagement.API.Application.Orders;

public sealed class OrderQueryService(OrderManagementDbCtx db)
{
  public Task<OrderDetailResponse?> GetOrderByIdAsync(
    Guid orderId, CancellationToken cancellationToken = default) =>
    db.Orders.AsNoTracking()
      .Where(order => order.Id == orderId)
      .Select(order => new OrderDetailResponse(
        order.Id,
        order.CustomerId,
        order.ShippingAddress,
        order.Status == OrderStatus.PENDING ? "Pending" :
          order.Status == OrderStatus.CONFIRMED ? "Confirmed" :
          order.Status == OrderStatus.SHIPPED ? "Shipped" :
          order.Status == OrderStatus.DELIVERED ? "Delivered" : "Cancelled",
        order.CreatedAtUtc,
        order.UpdatedAtUtc,
        order.Items.OrderBy(item => item.Id)
          .Select(item => new OrderDetailItemResponse(
            item.ProductId,
            item.OrderQuantity,
            item.UnitPrice))
          .ToList()))
      .SingleOrDefaultAsync(cancellationToken);
}
