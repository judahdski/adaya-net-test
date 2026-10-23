namespace AdayaOrderManagement.API.Application.Orders;

public sealed class OrderDetailResponse(
  Guid id,
  string customerId,
  string shippingAddress,
  string status,
  DateTime createdAtUtc,
  DateTime updatedAtUtc,
  IReadOnlyList<OrderDetailItemResponse> items)
{
  public Guid Id { get; } = id;
  public string CustomerId { get; } = customerId;
  public string ShippingAddress { get; } = shippingAddress;
  public string Status { get; } = status;
  public DateTime CreatedAtUtc { get; } = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
  public DateTime UpdatedAtUtc { get; } = DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc);
  public IReadOnlyList<OrderDetailItemResponse> Items { get; } = items;
  public decimal TotalAmount => Items.Sum(item => item.Subtotal);
}

public sealed record OrderDetailItemResponse(int ProductId, int Quantity, decimal UnitPrice)
{
  public decimal Subtotal => Quantity * UnitPrice;
}
