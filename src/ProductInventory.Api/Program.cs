using Microsoft.EntityFrameworkCore;
using ProductInventory.Api.Data;
using ProductInventory.Api.Endpoints;
using ProductInventory.Api.Images;
using ProductInventory.Api.OpenFoodFacts;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Inventory")
        ?? throw new InvalidOperationException("Connection string 'Inventory' is missing.")));

builder.Services.Configure<ImageStorageOptions>(builder.Configuration.GetSection(ImageStorageOptions.SectionName));
builder.Services.AddSingleton<IImageStorage, DiskImageStorage>();

var offOptions = builder.Configuration.GetSection(OpenFoodFactsOptions.SectionName).Get<OpenFoodFactsOptions>() ?? new();
builder.Services.AddHttpClient<IExternalProductLookup, OpenFoodFactsClient>(client =>
{
    client.BaseAddress = new Uri(offOptions.BaseUrl);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(offOptions.UserAgent);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health");
app.MapProductEndpoints();

app.Run();

public partial class Program;
