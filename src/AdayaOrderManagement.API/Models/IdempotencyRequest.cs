namespace AdayaOrderManagement.API.Models;

public sealed class IdempotencyRequest
{
  public string Key { get; set; } = string.Empty;
  public string PayloadHash { get; set; } = string.Empty;
  public Guid? OrderId { get; set; }
  public Order? Order { get; set; }
  public DateTime CreatedAtUtc { get; set; }
}
