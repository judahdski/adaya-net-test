using System.Net;
using System.Text.Json;
using AdayaOrderManagement.API.Infrastructure;
using AdayaOrderManagement.API.Models;
using AdayaOrderManagement.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdayaOrderManagement.Tests.Integration;

public sealed class GetOrderByIdTests
{
  [Fact]
  public async Task ReturnsOrderDetailsAndCalculatedAmountsOrProblemWhenMissing()
  {
    // ===== SETUP: bikin database sementara biar data aplikasi ga kesentuh =====
    var serverConnectionString = Environment.GetEnvironmentVariable("ADAYA_TEST_SQLSERVER_CONNECTION")
      ?? "Server=localhost;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
    var databaseName = $"AdayaOrderManagement_GetOrderTest_{Guid.NewGuid():N}";
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
      // ===== ARRANGE: jalanin API test dan simpan order dengan nilai yang gampang dicek =====
      using var factory = new CustomWebApplicationFactory(testConnection.ConnectionString);
      using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
      {
        BaseAddress = new Uri("https://localhost")
      });

      var orderId = Guid.NewGuid();
      await using (var seedScope = factory.Services.CreateAsyncScope())
      {
        var db = seedScope.ServiceProvider.GetRequiredService<OrderManagementDbCtx>();
        await db.Database.MigrateAsync();

        var firstProduct = new Product { Name = "First", Price = 150000m, StockQuantity = 10 };
        var secondProduct = new Product { Name = "Second", Price = 25000m, StockQuantity = 10 };
        db.Products.AddRange(firstProduct, secondProduct);
        await db.SaveChangesAsync();

        db.Orders.Add(new Order
        {
          Id = orderId,
          CustomerId = "CUST-001",
          ShippingAddress = "Jakarta Selatan",
          Status = OrderStatus.CONFIRMED,
          CreatedAtUtc = new DateTime(2026, 10, 23, 2, 0, 0, DateTimeKind.Utc),
          UpdatedAtUtc = new DateTime(2026, 10, 23, 2, 5, 0, DateTimeKind.Utc),
          Items =
          [
            new OrderItem { ProductId = firstProduct.Id, OrderQuantity = 2, UnitPrice = 150000m },
            new OrderItem { ProductId = secondProduct.Id, OrderQuantity = 3, UnitPrice = 25000m }
          ]
        });
        await db.SaveChangesAsync();
      }

      // ===== ASSERT SUCCESS: detail, subtotal, dan total harus sesuai data order =====
      using var response = await client.GetAsync($"/orders/{orderId}");
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
      var root = body.RootElement;
      Assert.Equal(orderId, root.GetProperty("id").GetGuid());
      Assert.Equal("CUST-001", root.GetProperty("customerId").GetString());
      Assert.Equal("Jakarta Selatan", root.GetProperty("shippingAddress").GetString());
      Assert.Equal("Confirmed", root.GetProperty("status").GetString());
      Assert.Equal("2026-10-23T02:00:00Z", root.GetProperty("createdAtUtc").GetString());
      Assert.Equal("2026-10-23T02:05:00Z", root.GetProperty("updatedAtUtc").GetString());
      Assert.Equal(375000m, root.GetProperty("totalAmount").GetDecimal());

      var items = root.GetProperty("items");
      Assert.Equal(2, items.GetArrayLength());
      Assert.Equal(2, items[0].GetProperty("quantity").GetInt32());
      Assert.Equal(150000m, items[0].GetProperty("unitPrice").GetDecimal());
      Assert.Equal(300000m, items[0].GetProperty("subtotal").GetDecimal());
      Assert.Equal(3, items[1].GetProperty("quantity").GetInt32());
      Assert.Equal(75000m, items[1].GetProperty("subtotal").GetDecimal());

      // ===== ASSERT ERROR: bedain order ga ketemu dengan format ID yang ga valid =====
      using var missing = await client.GetAsync($"/orders/{Guid.NewGuid()}");
      Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
      Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
      using var missingBody = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
      Assert.Equal("ORDER_NOT_FOUND", missingBody.RootElement.GetProperty("code").GetString());

      using var invalid = await client.GetAsync("/orders/invalid-id");
      Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    finally
    {
      // ===== CLEANUP: buang database sementara walaupun test-nya gagal =====
      using (var pooledConnection = new SqlConnection(testConnection.ConnectionString))
        SqlConnection.ClearPool(pooledConnection);
      await using var dropDatabase = master.CreateCommand();
      dropDatabase.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
      await dropDatabase.ExecuteNonQueryAsync();
    }
  }
}
