namespace AdayaOrderManagement.API.Application.Orders;

public sealed class CreateOrderRequest
{
  public string? CustomerId { get; set; }
  public string? ShippingAddress { get; set; }
  public List<CreateOrderItemRequest>? Items { get; set; }
}

public sealed class CreateOrderItemRequest
{
  public int ProductId { get; set; }
  public int Quantity { get; set; }
}
