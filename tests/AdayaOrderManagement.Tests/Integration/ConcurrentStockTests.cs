using System.Net;
using System.Net.Http.Json;
using AdayaOrderManagement.API.Application.Orders;
using AdayaOrderManagement.API.Infrastructure;
using AdayaOrderManagement.API.Models;
using AdayaOrderManagement.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdayaOrderManagement.Tests.Integration;

public sealed class ConcurrentStockTests
{
  [Fact]
  public async Task DifferentKeysForSameProduct_OnlyOneOrderDeductsStock()
  {
    var serverConnectionString = Environment.GetEnvironmentVariable("ADAYA_TEST_SQLSERVER_CONECTION")
      ?? "Server=localhost;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
    var databaseName = $"AdayaOrderManagement_ConcurrencyTest_{Guid.NewGuid():N}";
    var masterConnection = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = "master" };
    var testConnection = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = databaseName };

    await using var master = new SqlConnection(masterConnection.ConnectionString);
    await master.OpenAsync();

    // nama database selalu dibikin sendiri oleh test jadi database aplikasi ga bakal kesentuh
    await using (var createDatabase = master.CreateCommand())
    {
      createDatabase.CommandText = $"CREATE DATABASE [{databaseName}]";
      await createDatabase.ExecuteNonQueryAsync();
    }

    try
    {
      using var factory = new CustomWebApplicationFactory(testConnection.ConnectionString);
      using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
      {
        BaseAddress = new Uri("https://localhost")
      });

      int productId;
      await using (var seedScope = factory.Services.CreateAsyncScope())
      {
        var db = seedScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
        await db.Database.MigrateAsync();

        var product = new Product
        {
          Name = "Concurrent stock test product",
          StockQuantity = 15,
          Price = 100m
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        productId = product.Id;
      }

      var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

      async Task<HttpResponseMessage> SendOrderAsync(string key)
      {
        // dua request nunggu gate yang sama biar mulai sedekat mungkin
        await startGate.Task;
        using var message = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
          Content = JsonContent.Create(new
          {
            customerId = "C001",
            shippingAddress = "Jl. Melati No. 10",
            items = new[] { new { productId, quantity = 10 } }
          })
        };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
      }

      var requestA = SendOrderAsync("stock-request-A");
      var requestB = SendOrderAsync("stock-request-B");
      startGate.SetResult();
      var responses = await Task.WhenAll(requestA, requestB);
      using var responseA = responses[0];
      using var responseB = responses[1];

      Assert.Equal(
        [HttpStatusCode.Created, HttpStatusCode.Conflict],
        responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray());

      var created = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
      var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
      var createdOrder = await created.Content.ReadFromJsonAsync<CreateOrderResponse>();
      Assert.NotNull(createdOrder);
      Assert.Equal("PENDING", createdOrder.Status);
      Assert.Equal($"/orders/{createdOrder.OrderId}", created.Headers.Location?.ToString());

      using (var problem = await System.Text.Json.JsonDocument.ParseAsync(await conflict.Content.ReadAsStreamAsync()))
      {
        Assert.Equal("INSUFFICIENT_STOCK", problem.RootElement.GetProperty("code").GetString());
      }

      // context baru ini baca hasil yang udah benar benar tersimpan di sql server
      await using var verifyScope = factory.Services.CreateAsyncScope();
      var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
      var savedProduct = await verifyDb.Products.AsNoTracking().SingleAsync(x => x.Id == productId);
      var savedOrder = await verifyDb.Orders.AsNoTracking().Include(x => x.Items).SingleAsync();

      Assert.Equal(5, savedProduct.StockQuantity);
      Assert.True(savedProduct.StockQuantity >= 0);
      Assert.Equal(1, await verifyDb.Orders.CountAsync());
      Assert.Equal(createdOrder.OrderId, savedOrder.Id);
      var savedItem = Assert.Single(savedOrder.Items);
      Assert.Equal(productId, savedItem.ProductId);
      Assert.Equal(10, savedItem.OrderQuantity);
      Assert.Equal(1, await verifyDb.IdempotencyRequests.CountAsync());
    }
    finally
    {
      // yang dihapus cuma database acak yang barusan dibikin sama test ini
      using (var pooledConnection = new SqlConnection(testConnection.ConnectionString))
        SqlConnection.ClearPool(pooledConnection);
      await using var dropDatabase = master.CreateCommand();
      dropDatabase.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
      await dropDatabase.ExecuteNonQueryAsync();
    }
  }
}
