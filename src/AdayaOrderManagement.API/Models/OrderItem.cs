namespace AdayaOrderManagement.API.Models;

public sealed class OrderItem
{
  public int Id { get; set; }
  public Guid OrderId { get; set; }
  public Order Order { get; set; } = null!;
  public int ProductId { get; set; }
  public Product Product { get; set; } = null!;
  public int OrderQuantity { get; set; }
  public decimal UnitPrice { get; set; }
}