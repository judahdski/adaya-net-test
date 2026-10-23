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

public sealed class OrderStatusTests
{
  [Fact]
  public async Task CancellationRestoresStockOnceAndFailedRestoreRollsBack()
  {
    var serverConnectionString = Environment.GetEnvironmentVariable("ADAYA_TEST_SQLSERVER_CONNECTION")
      ?? "Server=localhost;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
    var databaseName = $"AdayaOrderManagement_StatusTest_{Guid.NewGuid():N}";
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
        var product = new Product { Name = "Status test product", StockQuantity = 10, Price = 100m };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        productId = product.Id;
      }

      using var missingOrder = await client.PatchAsJsonAsync(
        $"/orders/{Guid.NewGuid()}/status", new { status = "CONFIRMED" });
      Assert.Equal(HttpStatusCode.NotFound, missingOrder.StatusCode);

      using var invalidStatus = await client.PatchAsJsonAsync(
        $"/orders/{Guid.NewGuid()}/status", new { status = "Unknown" });
      Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);

      using var mixedCaseStatus = await client.PatchAsJsonAsync(
        $"/orders/{Guid.NewGuid()}/status", new { status = "Confirmed" });
      Assert.Equal(HttpStatusCode.BadRequest, mixedCaseStatus.StatusCode);

      using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/orders")
      {
        Content = JsonContent.Create(new
        {
          customerId = "C001",
          shippingAddress = "Jl. Melati No. 10",
          items = new[] { new { productId, quantity = 3 } }
        })
      };
      createRequest.Headers.Add("Idempotency-Key", "status-test-order");
      using var createResponse = await client.SendAsync(createRequest);
      Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
      var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResponse>();
      Assert.NotNull(created);
      Assert.Equal("PENDING", created.Status);

      await using (var statusConnection = new SqlConnection(testConnection.ConnectionString))
      {
        await statusConnection.OpenAsync();
        await using var readStatus = statusConnection.CreateCommand();
        readStatus.CommandText = "SELECT [Status] FROM [Orders] WHERE [Id] = @id";
        readStatus.Parameters.AddWithValue("@id", created.OrderId);
        Assert.Equal("PENDING", await readStatus.ExecuteScalarAsync());

        await using var lowerCaseStatus = statusConnection.CreateCommand();
        lowerCaseStatus.CommandText = "UPDATE [Orders] SET [Status] = 'pending' WHERE [Id] = @id";
        lowerCaseStatus.Parameters.AddWithValue("@id", created.OrderId);
        await Assert.ThrowsAsync<SqlException>(() => lowerCaseStatus.ExecuteNonQueryAsync());
      }

      await using (var firstScope = factory.Services.CreateAsyncScope())
      await using (var staleScope = factory.Services.CreateAsyncScope())
      {
        var firstDb = firstScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
        var firstOrder = await firstDb.Orders.SingleAsync(x => x.Id == created.OrderId);
        var staleOrder = await staleDb.Orders.SingleAsync(x => x.Id == created.OrderId);

        firstOrder.Status = OrderStatus.CONFIRMED;
        await firstDb.SaveChangesAsync();
        staleOrder.Status = OrderStatus.CANCELLED;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
      }

      using var cancelResponse = await client.PatchAsJsonAsync(
        $"/orders/{created.OrderId}/status", new { status = "CANCELLED" });
      Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
      var cancelled = await cancelResponse.Content.ReadFromJsonAsync<CreateOrderResponse>();
      Assert.Equal("CANCELLED", cancelled?.Status);

      var fetched = await client.GetFromJsonAsync<OrderDetailResponse>($"/orders/{created.OrderId}");
      Assert.Equal(created.OrderId, fetched?.Id);
      Assert.Equal("Cancelled", fetched?.Status);

      using var retryRequest = new HttpRequestMessage(HttpMethod.Post, "/orders")
      {
        Content = JsonContent.Create(new
        {
          customerId = "C001",
          shippingAddress = "Jl. Melati No. 10",
          items = new[] { new { productId, quantity = 3 } }
        })
      };
      retryRequest.Headers.Add("Idempotency-Key", "status-test-order");
      using var retryResponse = await client.SendAsync(retryRequest);
      Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
      var retried = await retryResponse.Content.ReadFromJsonAsync<CreateOrderResponse>();
      Assert.Equal("CANCELLED", retried?.Status);

      using var secondCancel = await client.PatchAsJsonAsync(
        $"/orders/{created.OrderId}/status", new { status = "CANCELLED" });
      Assert.Equal(HttpStatusCode.Conflict, secondCancel.StatusCode);

      await using var verifyScope = factory.Services.CreateAsyncScope();
      var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
      var savedOrder = await verifyDb.Orders.AsNoTracking().SingleAsync(x => x.Id == created.OrderId);
      var savedProduct = await verifyDb.Products.AsNoTracking().SingleAsync(x => x.Id == productId);
      Assert.Equal(OrderStatus.CANCELLED, savedOrder.Status);
      Assert.Equal(10, savedProduct.StockQuantity);

      await using (var statusConnection = new SqlConnection(testConnection.ConnectionString))
      {
        await statusConnection.OpenAsync();
        await using var readStatus = statusConnection.CreateCommand();
        readStatus.CommandText = "SELECT [Status] FROM [Orders] WHERE [Id] = @id";
        readStatus.Parameters.AddWithValue("@id", created.OrderId);
        Assert.Equal("CANCELLED", await readStatus.ExecuteScalarAsync());
      }

      using var secondCreateRequest = new HttpRequestMessage(HttpMethod.Post, "/orders")
      {
        Content = JsonContent.Create(new
        {
          customerId = "C002",
          shippingAddress = "Jl. Mawar No. 2",
          items = new[] { new { productId, quantity = 1 } }
        })
      };
      secondCreateRequest.Headers.Add("Idempotency-Key", "status-test-restore-failure");
      using var secondCreateResponse = await client.SendAsync(secondCreateRequest);
      Assert.Equal(HttpStatusCode.Created, secondCreateResponse.StatusCode);
      var secondOrder = await secondCreateResponse.Content.ReadFromJsonAsync<CreateOrderResponse>();
      Assert.NotNull(secondOrder);

      await using (var changeStockScope = factory.Services.CreateAsyncScope())
      {
        var changeStockDb = changeStockScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
        var product = await changeStockDb.Products.SingleAsync(x => x.Id == productId);
        product.StockQuantity = int.MaxValue;
        await changeStockDb.SaveChangesAsync();
      }

      using var failedCancel = await client.PatchAsJsonAsync(
        $"/orders/{secondOrder.OrderId}/status", new { status = "CANCELLED" });
      Assert.Equal(HttpStatusCode.Conflict, failedCancel.StatusCode);

      await using var failedVerifyScope = factory.Services.CreateAsyncScope();
      var failedVerifyDb = failedVerifyScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
      var unchangedOrder = await failedVerifyDb.Orders.AsNoTracking()
        .SingleAsync(x => x.Id == secondOrder.OrderId);
      var unchangedProduct = await failedVerifyDb.Products.AsNoTracking()
        .SingleAsync(x => x.Id == productId);
      Assert.Equal(OrderStatus.PENDING, unchangedOrder.Status);
      Assert.Equal(int.MaxValue, unchangedProduct.StockQuantity);
    }
    finally
    {
      using (var pooledConnection = new SqlConnection(testConnection.ConnectionString))
        SqlConnection.ClearPool(pooledConnection);
      await using var dropDatabase = master.CreateCommand();
      dropDatabase.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
      await dropDatabase.ExecuteNonQueryAsync();
    }
  }
}
