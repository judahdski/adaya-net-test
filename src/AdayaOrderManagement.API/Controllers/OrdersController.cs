using AdayaOrderManagement.API.Application.Orders;
using AdayaOrderManagement.API.Models;
using Microsoft.AspNetCore.Mvc;

namespace AdayaOrderManagement.API.Controllers;

[ApiController]
[Route("orders")]
public sealed class OrdersController(CreateOrderService service, OrderStatusService statusService, OrderQueryService queryService, ILogger<OrdersController> logger) : ControllerBase
{
  [HttpPost]
  public async Task<IActionResult> Create(
    [FromBody] CreateOrderRequest? request,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    CancellationToken cancellationToken)
  {
    // ngecek request dulu
    var errors = CreateOrderValidator.Validate(request, idempotencyKey);

    // kalo ada error dari hasil validate langsung direturn dengan code 400
    if (errors.Count != 0)
    {
      var problem = new ValidationProblemDetails(errors)
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Invalid request"
      };
      problem.Extensions["code"] = "INVALID_REQUEST";
      problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;

      return ProblemResult(problem);
    }

    var result = await service.CreateAsync(request!, idempotencyKey!, cancellationToken);

    // order baru dapat 201, retry yang cocok dapat 200 tanpa bikin order lagi
    return result.Outcome switch
    {
      CreateOrderOutcome.Created => Created($"/orders/{result.Order!.OrderId}", result.Order),
      CreateOrderOutcome.Existing => Ok(result.Order),
      CreateOrderOutcome.KeyReused => Error(409, "Idempotency key reused", "This key was used with a different payload.", "IDEMPOTENCY_KEY_REUSED"),
      CreateOrderOutcome.ProductNotFound => Error(422, "Product not found", $"Product {result.ProductId} was not found.", "PRODUCT_NOT_FOUND"),
      CreateOrderOutcome.InsufficientStock => Error(409, "Insufficient stock", $"Stock for product {result.ProductId} is insufficient.", "INSUFFICIENT_STOCK"),
      _ => throw new InvalidOperationException("Unexpected order result.")
    };
  }

  [HttpGet("{id}")]
  public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
  {
    logger.LogInformation("Tunggu yaa....");

    if (!Guid.TryParse(id, out var orderId))
      return Error(400, "Invalid request", "A valid order ID is required.", "INVALID_REQUEST");

    var order = await queryService.GetOrderByIdAsync(orderId, cancellationToken);
    return order is null
      ? Error(404, "Order not found", $"Order {orderId} was not found.", "ORDER_NOT_FOUND")
      : Ok(order);
  }

  [HttpPatch("{id:guid}/status")]
  public async Task<IActionResult> UpdateStatus(
    Guid id, [FromBody] UpdateOrderStatusRequest? request, CancellationToken cancellationToken)
  {
    // cuma nama status uppercase yang dikenal yang boleh masuk ke alur perubahan order
    var statusName = Enum.GetNames<OrderStatus>()
      .FirstOrDefault(x => string.Equals(x, request?.Status, StringComparison.Ordinal));
    if (statusName is null)
      return Error(400, "Invalid request", "A valid status is required.", "INVALID_REQUEST");

    var targetStatus = Enum.Parse<OrderStatus>(statusName);
    var result = await statusService.UpdateAsync(id, targetStatus, cancellationToken);
    return result.Outcome switch
    {
      UpdateOrderStatusOutcome.Updated => Ok(result.Order),
      UpdateOrderStatusOutcome.NotFound => Error(404, "Order not found", $"Order {id} was not found.", "ORDER_NOT_FOUND"),
      UpdateOrderStatusOutcome.InvalidTransition => Error(409, "Invalid status transition", "The requested status cannot follow the current status.", "INVALID_STATUS_TRANSITION"),
      UpdateOrderStatusOutcome.ConcurrencyConflict => Error(409, "Order changed", "The order was changed by another request. Try again.", "ORDER_CONCURRENCY_CONFLICT"),
      UpdateOrderStatusOutcome.StockRestoreFailed => Error(409, "Stock restore failed", "The order was not cancelled because stock could not be restored.", "STOCK_RESTORE_FAILED"),
      _ => throw new InvalidOperationException("Unexpected status update result.")
    };
  }

  private IActionResult Error(int status, string title, string detail, string code)
  {
    var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
    problem.Extensions["code"] = code;
    problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
    return ProblemResult(problem);
  }

  private static ObjectResult ProblemResult(ProblemDetails problem) => new(problem)
  {
    StatusCode = problem.Status,
    ContentTypes = { "application/problem+json" }
  };
}
