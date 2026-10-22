using AdayaOrderManagement.API.Application.Orders;

namespace AdayaOrderManagement.Tests;

public class CreateOrderTests
{
  [Fact]
  public void EquivalentItemOrderProducesSameHash()
  {
    var first = new CreateOrderRequest
    {
      CustomerId = "C001",
      ShippingAddress = "Jl. Melati No. 10",
      Items =
      [
        new() { ProductId = 2, Quantity = 1 },
        new() { ProductId = 1, Quantity = 3 }
      ]
    };
    var reordered = new CreateOrderRequest
    {
      CustomerId = "C001",
      ShippingAddress = "Jl. Melati No. 10",
      Items =
      [
        new() { ProductId = 1, Quantity = 3 },
        new() { ProductId = 2, Quantity = 1 }
      ]
    };

    Assert.Equal(OrderPayloadHasher.Compute(first), OrderPayloadHasher.Compute(reordered));
    Assert.Equal(64, OrderPayloadHasher.Compute(first).Length);
  }

  [Fact]
  public void ChangedQuantityProducesDifferentHash()
  {
    var request = new CreateOrderRequest
    {
      CustomerId = "C001",
      ShippingAddress = "Address",
      Items = [new() { ProductId = 1, Quantity = 1 }]
    };
    var original = OrderPayloadHasher.Compute(request);
    request.Items![0].Quantity = 2;

    Assert.NotEqual(original, OrderPayloadHasher.Compute(request));
  }

  [Fact]
  public void RejectsDuplicateProductAndInvalidQuantity()
  {
    var request = new CreateOrderRequest
    {
      CustomerId = "C001",
      ShippingAddress = "Address",
      Items =
      [
        new() { ProductId = 1, Quantity = 1 },
        new() { ProductId = 1, Quantity = 0 }
      ]
    };

    var errors = CreateOrderValidator.Validate(request, "request-1");

    Assert.Contains("items[1].productId", errors.Keys);
    Assert.Contains("items[1].quantity", errors.Keys);
  }

  [Fact]
  public void RejectsMissingKeyAndWhitespaceFields()
  {
    var request = new CreateOrderRequest
    {
      CustomerId = " ",
      ShippingAddress = " ",
      Items = [new() { ProductId = 1, Quantity = 1 }]
    };

    var errors = CreateOrderValidator.Validate(request, " ");

    Assert.Contains("Idempotency-Key", errors.Keys);
    Assert.Contains("customerId", errors.Keys);
    Assert.Contains("shippingAddress", errors.Keys);
  }
}
