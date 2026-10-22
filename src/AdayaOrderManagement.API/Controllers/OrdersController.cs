using AdayaOrderManagement.API.Application.Orders;
using AdayaOrderManagement.API.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdayaOrderManagement.API.Controllers;

[ApiController]
[Route("orders")]
public sealed class OrdersController(CreateOrderService service, OrderManagementDbCtx db) : ControllerBase
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

  [HttpGet("{id:guid}")]
  public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
  {
    var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    return order is null ? NotFound() : Ok(CreateOrderResponse.FromOrder(order));
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
