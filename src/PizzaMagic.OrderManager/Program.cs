using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.OrderManager.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("PizzaMagic")
    ?? throw new InvalidOperationException("Connection string 'PizzaMagic' is missing.");

builder.Services.AddDbContext<PizzaMagicDbContext>(options => options.UsePizzaMagicSqlite(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(PromotionRegistry.Default);
builder.Services.AddSingleton(PricingMethodRegistry.Default);
builder.Services.AddScoped(services =>
    new CatalogLoader(services.GetRequiredService<PizzaMagicDbContext>(), services.GetRequiredService<PricingMethodRegistry>()));

builder.Services.AddControllers();
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
