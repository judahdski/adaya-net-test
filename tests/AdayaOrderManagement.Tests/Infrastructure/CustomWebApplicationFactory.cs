using AdayaOrderManagement.API.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdayaOrderManagement.Tests.Infrastructure;

public sealed class CustomWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Development");
    builder.ConfigureTestServices(services =>
    {
      // arahin db context ke database sementara yang dibikin khusus buat test ini
      services.RemoveAll<DbContextOptions<OrderManagementDbCtx>>();
      services.AddDbContext<OrderManagementDbCtx>(options => options.UseSqlServer(connectionString));
    });
  }
}
