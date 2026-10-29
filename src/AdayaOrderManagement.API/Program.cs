using AdayaOrderManagement.API.Application.Orders;
using AdayaOrderManagement.API.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// daftarkan layanan yang dipakai aplikasi
builder.Services.AddDbContext<OrderManagementDbCtx>(
    options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
);
builder.Services.AddScoped<CreateOrderService>();
builder.Services.AddScoped<OrderStatusService>();
builder.Services.AddScoped<OrderQueryService>();

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

// TODO: cari tau ini tuh istilahnya apa? RequestMiddleware? semua request pasti lewat sini? trus 'logger.BeginScope' namanya tuh apa? logging scope?

app.Use(async (context, next) =>
{
    var correlationId = context.TraceIdentifier;

    // id ini ikut di header supaya respons error gampang dicocokkan dengan log
    context.Response.Headers["X-Correlation-ID"] = correlationId;

    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AdayaOrderManagement.API.Request");

    using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

            var logLevel = statusCode >= 500 ? LogLevel.Error : statusCode >= 400 ? LogLevel.Warning : LogLevel.Information;

            logger.Log(logLevel, "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms", context.Request.Method, context.Request.Path.Value, statusCode, elapsedMs);
        }
    }
});

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    // error tak terduga tetap dicatat, tapi detail database tidak dikirim ke klien
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");

    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

    logger.LogError(exception, "CustomErrorHandler || Unhandled request error {CorrelationId}", context.TraceIdentifier);

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
    app.MapGet("/test-exception", () =>
    {
        throw new InvalidOperationException("Simulated unexpected exception");
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();