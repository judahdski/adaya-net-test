using AdayaOrderManagement.API.Application.Orders;
using AdayaOrderManagement.API.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// daftarkan layanan yang dipakai aplikasi
builder.Services.AddDbContext<OrderManagementDbCtx>(
    options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
);
builder.Services.AddScoped<CreateOrderService>();

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid request"
        };
        problem.Extensions["code"] = "INVALID_REQUEST";
        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    };
});
// panduan konfigurasi openapi ada di https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.Use(async (context, next) =>
{
    // id ini ikut di header supaya respons error gampang dicocokkan dengan log
    context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
    await next(context);
});

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    // error tak terduga tetap dicatat, tapi detail database tidak dikirim ke klien
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    logger.LogError(exception, "Unhandled request error {CorrelationId}", context.TraceIdentifier);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new
    {
        type = "about:blank",
        title = "Internal server error",
        status = 500,
        code = "INTERNAL_ERROR",
        correlationId = context.TraceIdentifier
    }, options: null, contentType: "application/problem+json");
}));

// atur alur pemrosesan request http
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
