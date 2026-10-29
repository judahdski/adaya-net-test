using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AdayaOrderManagement.API.Application.Orders;
using AdayaOrderManagement.API.Infrastructure;
using AdayaOrderManagement.API.Models;
using AdayaOrderManagement.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace AdayaOrderManagement.Tests.Integration;

public sealed class IdempotencyTests
{
  [Fact]
  public async Task ConcurrentIdenticalRequests_CreateOneOrderAndDeductStockOnce()
  {
    // ===== SETUP: bikin database sementara biar data aplikasi ga kesentuh =====
    var serverConnectionString = Environment.GetEnvironmentVariable("ADAYA_TEST_SQLSERVER_CONNECTION")
      ?? "Server=localhost;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
    var databaseName = $"AdayaOrderManagement_IdempotencyTest_{Guid.NewGuid():N}";
    var masterConnection = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = "master" };
    var testConnection = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = databaseName };

    await using var master = new SqlConnection(masterConnection.ConnectionString);
    await master.OpenAsync();
    await using (var createDatabase = master.CreateCommand())
    {
      createDatabase.CommandText = $"CREATE DATABASE [{databaseName}]";
      await createDatabase.ExecuteNonQueryAsync();
    }

    try
    {
      // ===== SETUP API: pasang barrier test ke HTTP pipeline dan database asli =====
      const string key = "scenario-C-same-logical-operation";
      var claimBarrier = new ConcurrentClaimBarrier(key);
      using var factory = new CustomWebApplicationFactory(testConnection.ConnectionString, claimBarrier);
      using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
      {
        BaseAddress = new Uri("https://localhost")
      });

      // ===== ARRANGE: stock 15 dan database belum punya order atau claim =====
      // database baru per test, jadi ga nyentuh data aplikasi.
      int productId;
      await using (var seedScope = factory.Services.CreateAsyncScope())
      {
        var db = seedScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
        await db.Database.MigrateAsync();
        var product = new Product { Name = "Product X", StockQuantity = 15, Price = 100m };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        productId = product.Id;
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Equal(0, await db.IdempotencyRequests.CountAsync());
      }

      // ===== PREPARE: dua message terpisah, tapi payload dan key-nya sama =====
      var payload = new
      {
        customerId = "C001",
        shippingAddress = "Jl. Melati No. 10",
        items = new[] { new { productId, quantity = 10 } }
      };
      HttpRequestMessage PrepareRequest()
      {
        var message = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
          Content = JsonContent.Create(payload)
        };
        message.Headers.Add("Idempotency-Key", key);
        return message;
      }

      using var requestA = PrepareRequest();
      using var requestB = PrepareRequest();
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

      // ===== ACT: kirim dua request dulu, baru tunggu hasil keduanya =====
      // Jangan await request A sebelum mengirim B. Barrier di bawah memaksa
      // dua transaksi mencapai penyimpanan klaim sebelum salah satunya boleh lanjut.
      var taskA = client.SendAsync(requestA, timeout.Token);
      var taskB = client.SendAsync(requestB, timeout.Token);
      var responses = await Task.WhenAll(taskA, taskB);
      using var responseA = responses[0];
      using var responseB = responses[1];

      // ===== ASSERT HTTP: dua request harus ketemu di claim dan dapet order yang sama =====
      Assert.Equal(2, claimBarrier.ArrivedContextCount);
      Assert.Equal(
        [HttpStatusCode.OK, HttpStatusCode.Created],
        responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray());

      var orderA = await responseA.Content.ReadFromJsonAsync<CreateOrderResponse>();
      var orderB = await responseB.Content.ReadFromJsonAsync<CreateOrderResponse>();
      Assert.NotNull(orderA);
      Assert.NotNull(orderB);
      Assert.NotEqual(Guid.Empty, orderA.OrderId);
      Assert.Equal(orderA.OrderId, orderB.OrderId);
      Assert.Equal("PENDING", orderA.Status);
      Assert.Equal("PENDING", orderB.Status);
      var created = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
      Assert.Equal($"/orders/{orderA.OrderId}", created.Headers.Location?.ToString());

      // ===== ASSERT DATABASE: cuma boleh ada satu order dan satu pengurangan stock =====
      // Pake DbContext baru buat baca hasil commit tanpa tracking.
      await using var verifyScope = factory.Services.CreateAsyncScope();
      var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
      Assert.Equal(1, await verifyDb.Orders.CountAsync());
      Assert.Equal(1, await verifyDb.IdempotencyRequests.CountAsync());
      Assert.Equal(1, await verifyDb.OrderItems.CountAsync());
      var savedOrder = await verifyDb.Orders.AsNoTracking().Include(x => x.Items).SingleAsync();
      var savedClaim = await verifyDb.IdempotencyRequests.AsNoTracking().SingleAsync();
      var savedProduct = await verifyDb.Products.AsNoTracking().SingleAsync(x => x.Id == productId);

      Assert.Equal(orderA.OrderId, savedOrder.Id);
      Assert.Equal(payload.customerId, savedOrder.CustomerId);
      Assert.Equal(payload.shippingAddress, savedOrder.ShippingAddress);
      Assert.Equal(OrderStatus.PENDING, savedOrder.Status);
      Assert.Equal(key, savedClaim.Key);
      Assert.Equal(savedOrder.Id, savedClaim.OrderId);
      var savedItem = Assert.Single(savedOrder.Items);
      Assert.Equal(productId, savedItem.ProductId);
      Assert.Equal(10, savedItem.OrderQuantity);
      Assert.Equal(100m, savedItem.UnitPrice);
      Assert.Equal(5, savedProduct.StockQuantity);
    }
    finally
    {
      // ===== CLEANUP: buang database sementara walaupun test-nya gagal =====
      // Hapus hanya database bernama acak yang dibuat oleh test ini.
      using (var pooledConnection = new SqlConnection(testConnection.ConnectionString))
        SqlConnection.ClearPool(pooledConnection);
      await using var dropDatabase = master.CreateCommand();
      dropDatabase.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
      await dropDatabase.ExecuteNonQueryAsync();
    }
  }

  // ===== TEST HOOK: bikin race-nya konsisten tanpa nge-mock service atau SQL =====
  // Hook cuma buat scheduling test; SQL, transaksi, dan service tetap asli.
  // Jangan taruh barrier setelah INSERT: request kedua bisa menunggu lock milik
  // request pertama, sementara request pertama menunggu barrier (deadlock test).
  private sealed class ConcurrentClaimBarrier(string key) : SaveChangesInterceptor
  {
    private readonly ConcurrentDictionary<Guid, byte> contexts = new();
    private readonly TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ArrivedContextCount => contexts.Count;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default)
    {
      var db = eventData.Context;
      if (db is null || !db.ChangeTracker.Entries<IdempotencyRequest>()
          .Any(entry => entry.State == EntityState.Added && entry.Entity.Key == key))
        return result;

      if (db.Database.CurrentTransaction is null)
        throw new InvalidOperationException("Idempotency claim must be saved inside a transaction.");

      contexts.TryAdd(db.ContextId.InstanceId, 0);
      if (contexts.Count == 2)
        bothArrived.TrySetResult();

      // Sequential execution atau request yang tidak mencapai klaim harus gagal,
      // bukan diam-diam lolos sebagai test concurrency atau menggantung selamanya.
      await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
      return result;
    }
  }
}
