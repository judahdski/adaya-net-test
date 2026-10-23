using AdayaOrderManagement.API.Application.Orders;
using AdayaOrderManagement.API.Models;

namespace AdayaOrderManagement.Tests;

public sealed class OrderStatusTransitionTests
{
  [Theory]
  [InlineData(OrderStatus.PENDING, OrderStatus.CONFIRMED)]
  [InlineData(OrderStatus.PENDING, OrderStatus.CANCELLED)]
  [InlineData(OrderStatus.CONFIRMED, OrderStatus.SHIPPED)]
  [InlineData(OrderStatus.CONFIRMED, OrderStatus.CANCELLED)]
  [InlineData(OrderStatus.SHIPPED, OrderStatus.DELIVERED)]
  public void AllowsExpectedTransitions(OrderStatus current, OrderStatus target) =>
    Assert.True(OrderStatusService.IsValidTransition(current, target));

  [Theory]
  [InlineData(OrderStatus.PENDING, OrderStatus.PENDING)]
  [InlineData(OrderStatus.PENDING, OrderStatus.SHIPPED)]
  [InlineData(OrderStatus.SHIPPED, OrderStatus.CANCELLED)]
  [InlineData(OrderStatus.DELIVERED, OrderStatus.CANCELLED)]
  [InlineData(OrderStatus.CANCELLED, OrderStatus.CONFIRMED)]
  public void RejectsInvalidTransitions(OrderStatus current, OrderStatus target) =>
    Assert.False(OrderStatusService.IsValidTransition(current, target));
}
