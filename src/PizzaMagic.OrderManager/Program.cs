using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.OrderManager.Api;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("PizzaMagic")
    ?? throw new InvalidOperationException("Connection string 'PizzaMagic' is missing.");

builder.Services.AddDbContext<PizzaMagicDbContext>(options => options.UsePizzaMagicSqlite(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(PromotionRegistry.Default);
builder.Services.AddSingleton(PricingMethodRegistry.Default);
builder.Services.AddScoped(services =>
    new CatalogLoader(services.GetRequiredService<PizzaMagicDbContext>(), services.GetRequiredService<PricingMethodRegistry>()));
builder.Services.AddSingleton(services =>
    new PricingEngine(new PromotionEngine(services.GetRequiredService<PromotionRegistry>(), new RejectLaterConflicts())));
builder.Services.AddScoped<OrderPricer>();
builder.Services.AddScoped(services => new OrderService(
    services.GetRequiredService<PizzaMagicDbContext>(),
    services.GetRequiredService<OrderPricer>(),
    services.GetRequiredService<CatalogLoader>(),
    services.GetRequiredService<TimeProvider>(),
    // The stored replay body must be serialized exactly as controller responses are.
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JsonOptions>>().Value.JsonSerializerOptions,
    services.GetRequiredService<IConfiguration>()));

builder.Services.AddProblemDetails();
// Request fields are nullable on purpose: the Domain validator reports every problem with its path.
builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(options =>
    {
        // Unknown properties are a 400 (plan section 5): a typo must not be silently ignored.
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Malformed JSON or an unknown property: 400 with the same validation problem shape.
        options.InvalidModelStateResponseFactory = context => Problems.Validation(
            context.ModelState
                .Where(entry => entry.Value is { Errors.Count: > 0 })
                .SelectMany(entry => entry.Value!.Errors.Select(error => new FieldError(
                    string.IsNullOrEmpty(entry.Key) ? "$" : entry.Key,
                    "INVALID_REQUEST",
                    string.IsNullOrEmpty(error.ErrorMessage) ? "The request body is not valid JSON for this endpoint." : error.ErrorMessage))),
            StatusCodes.Status400BadRequest);
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Startup (plan section 4.6): migrate (schema only), WAL, seed scripts, then validate the catalog; refuse to start if invalid.
await using (var scope = app.Services.CreateAsyncScope())
{
    var services = scope.ServiceProvider;
    await DatabaseInitializer.InitializeAsync(
        services.GetRequiredService<PizzaMagicDbContext>(),
        Path.Combine(AppContext.BaseDirectory, "data"),
        services.GetRequiredService<TimeProvider>(),
        services.GetRequiredService<PromotionRegistry>(),
        services.GetRequiredService<PricingMethodRegistry>());
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
