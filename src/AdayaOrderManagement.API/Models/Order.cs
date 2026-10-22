using System.Data;

namespace AdayaOrderManagement.API.Models;

public sealed class Order
{
  public Guid Id { get; set; }
  public string CustomerId { get; set; } = string.Empty;
  public string ShippingAddress { get; set; } = string.Empty;
  public OrderStatus Status { get; set; }
  public DateTime CreatedAtUtc { get; set; }
  public DateTime UpdatedAtUtc { get; set; }
  public byte[] RowVersion { get; set; }

  public ICollection<OrderItem> Items { get; set; }
}

public enum OrderStatus
{
  PENDING, CONFIRMED, SHIPPED, DELIVERED, CANCELLED
}