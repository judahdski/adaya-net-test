namespace AdayaOrderManagement.API.Application.Orders;

public static class CreateOrderValidator
{
  public static Dictionary<string, string[]> Validate(CreateOrderRequest? request, string? idempotencyKey)
  {
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
      errors["Idempotency-Key"] = ["Idempotency-Key is required and must be at most 200 characters."];

    if (request is null)
    {
      errors["body"] = ["A request body is required."];
      return errors;
    }

    if (string.IsNullOrWhiteSpace(request.CustomerId))
      errors["customerId"] = ["Customer ID is required."];

    if (string.IsNullOrWhiteSpace(request.ShippingAddress))
      errors["shippingAddress"] = ["Shipping address is required."];

    if (request.Items is null || request.Items.Count == 0)
    {
      errors["items"] = ["At least one item is required."];
      return errors;
    }

    var productIds = new HashSet<int>();
    for (var i = 0; i < request.Items.Count; i++)
    {
      var item = request.Items[i];
      if (item is null)
      {
        errors[$"items[{i}]"] = ["Item is required."];
        continue;
      }

      if (item.ProductId <= 0)
        errors[$"items[{i}].productId"] = ["Product ID must be greater than zero."];
      else if (!productIds.Add(item.ProductId))
        errors[$"items[{i}].productId"] = ["Product ID must be unique within the order."];

      if (item.Quantity <= 0)
        errors[$"items[{i}].quantity"] = ["Quantity must be greater than zero."];
    }

    return errors;
  }
}
