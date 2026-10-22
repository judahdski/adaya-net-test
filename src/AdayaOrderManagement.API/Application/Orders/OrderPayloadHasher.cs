using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AdayaOrderManagement.API.Application.Orders;

public static class OrderPayloadHasher
{
  // dipanggil habis validate request: setiap item itu harus punya ID yang unik dan harus >=0
  public static string Compute(CreateOrderRequest request)
  {
    // buat ulang payload biar urutan property JSONnya tetap sama
    var canonicalPayload = new
    {
      request.CustomerId,
      request.ShippingAddress,
      Items = request.Items!.OrderBy(x => x.ProductId)
        .Select(x => new { x.ProductId, x.Quantity }).ToArray()
    };

    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonicalPayload));
    return Convert.ToHexString(SHA256.HashData(bytes));
  }
}
