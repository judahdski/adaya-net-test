using AdayaOrderManagement.API.Infrastructure;
using AdayaOrderManagement.API.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AdayaOrderManagement.API.Application.Orders;

public enum CreateOrderOutcome
{
  Created,
  Existing,
  KeyReused,
  ProductNotFound,
  InsufficientStock
}

public sealed record CreateOrderResult(
  CreateOrderOutcome Outcome,
  CreateOrderResponse? Order = null,
  int? ProductId = null);

public sealed class CreateOrderService(OrderManagementDbCtx db)
{
  public async Task<CreateOrderResult> CreateAsync(
    CreateOrderRequest request, string idempotencyKey, CancellationToken cancellationToken)
  {
    // hash dihitung sebelum transaksi
    // semua perubahan data di bawah ini harus berhasil bareng-bareng
    var payloadHash = OrderPayloadHasher.Compute(request);

    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

    // simpan key dulu supaya request lain dengan key yang sama harus nunggu hasil transaksi ini
    var claim = new IdempotencyRequest
    {
      Key = idempotencyKey,
      PayloadHash = payloadHash,
      CreatedAtUtc = DateTime.UtcNow
    };
    db.IdempotencyRequests.Add(claim);

    try
    {
      await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException ex) when (IsDuplicateKey(ex))
    {
      // key udah dipake: batalin transaksi ini
      await transaction.RollbackAsync(cancellationToken);

      db.Entry(claim).State = EntityState.Detached;

      // ambil data existing
      var existing = await db.IdempotencyRequests.AsNoTracking()
        .SingleAsync(x => x.Key == idempotencyKey, cancellationToken);

      // key yang sama cuma boleh dipakai ulang kalau isi request-nya juga sama
      if (existing.PayloadHash != payloadHash)
        return new(CreateOrderOutcome.KeyReused);

      // key yang sudah tersimpan pasti terhubung ke order karena keduanya ditulis dalam satu transaksi
      var order = await db.Orders.AsNoTracking()
        .SingleAsync(x => x.Id == existing.OrderId, cancellationToken);
      return new(CreateOrderOutcome.Existing, CreateOrderResponse.FromOrder(order));
    }

    var requestedItems = request.Items!.OrderBy(x => x.ProductId).ToArray();
    var productIds = requestedItems.Select(x => x.ProductId).ToArray();

    // harga yang dibaca di sini jadi harga tetap untuk item order yang akan disimpan
    var products = await db.Products.AsNoTracking()
      .Where(x => productIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, cancellationToken);

    foreach (var item in requestedItems)
    {
      if (!products.ContainsKey(item.ProductId))
      {
        await transaction.RollbackAsync(cancellationToken);
        return new(CreateOrderOutcome.ProductNotFound, ProductId: item.ProductId);
      }
    }

    foreach (var item in requestedItems)
    {
      // conditional update bikin pengecekan dan pengurangan stok terjadi sekaligus
      // di sini, salah satu cara ngehandle si race condition
      var updated = await db.Products
        .Where(x => x.Id == item.ProductId && x.StockQuantity >= item.Quantity)
        .ExecuteUpdateAsync(
          setters => setters.SetProperty(x => x.StockQuantity, x => x.StockQuantity - item.Quantity),
          cancellationToken);

      if (updated != 1)
      {
        await transaction.RollbackAsync(cancellationToken);
        return new(CreateOrderOutcome.InsufficientStock, ProductId: item.ProductId);
      }
    }

    var now = DateTime.UtcNow;
    // simpan jumlah dan harga per item sebagai snapshot saat order dibuat
    var orderToSave = new Order
    {
      Id = Guid.NewGuid(),
      CustomerId = request.CustomerId!,
      ShippingAddress = request.ShippingAddress!,
      Status = OrderStatus.PENDING,
      CreatedAtUtc = now,
      UpdatedAtUtc = now,
      Items = requestedItems.Select(item => new OrderItem
      {
        ProductId = item.ProductId,
        OrderQuantity = item.Quantity,
        UnitPrice = products[item.ProductId].Price
      }).ToList()
    };

    db.Orders.Add(orderToSave);

    claim.OrderId = orderToSave.Id;
    await db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    return new(CreateOrderOutcome.Created, CreateOrderResponse.FromOrder(orderToSave));
  }

  private static bool IsDuplicateKey(DbUpdateException exception) =>
    exception.InnerException is SqlException sql && sql.Number is 2601 or 2627;
}
