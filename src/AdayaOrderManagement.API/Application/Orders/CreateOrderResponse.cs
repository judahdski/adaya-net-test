namespace AdayaOrderManagement.API.Application.Orders;

public sealed record CreateOrderResponse(Guid OrderId, string Status)
{
  public static CreateOrderResponse FromOrder(Models.Order order) =>
    new(order.Id, Enum.GetName(order.Status)![..1] + Enum.GetName(order.Status)![1..].ToLowerInvariant());
}
