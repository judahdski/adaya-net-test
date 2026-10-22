namespace AdayaOrderManagement.API.Models;

public sealed class IdempotencyRequest
{
  public string Key { get; set; }
  public char PayloadHash { get; set; }
  public Guid? OrderId { get; set; }
  public DateTime CreatedAtUtc { get; set; }
}