using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PizzaMagic.Domain.Catalog;
using PizzaMagic.Domain.Pricing;
using PizzaMagic.Domain.Promotions;
using PizzaMagic.OrderManager.Api;
using PizzaMagic.OrderManager.Data;
using PizzaMagic.OrderManager.Services;

var builder = WebApplication.CreateBuilder(args);

// The largest legitimate order is a few KB; refuse anything near a megabyte before it is read.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1024 * 1024);

// M15F: --database default|fantasy (= Seed:Profile) picks the seed data and its file; both files sit side by side in
// the working folder (pizzamagic.db, pizzamagic-fantasy.db). An explicit ConnectionStrings:PizzaMagic wins over the file.
// A bad option, or a file holding the other dataset, stops startup with one line and exit code 2.
builder.Configuration.AddCommandLine(args, DatabaseSelection.SwitchMappings);
DatabaseSelection database;
try
{
    DatabaseSelection.CheckArguments(args);
    database = DatabaseSelection.Resolve(builder.Configuration, Environment.CurrentDirectory);
}
catch (DatabaseProfileException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

builder.Services.AddDbContext<PizzaMagicDbContext>(options => options.UsePizzaMagicSqlite(database.ConnectionString));
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
    var db = services.GetRequiredService<PizzaMagicDbContext>();
    app.Logger.LogInformation(
        "Database profile {Profile}: {File}{Source}",
        database.Profile,
        Path.GetFullPath(new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource),
        database.IsExplicitConnectionString ? " (from ConnectionStrings:PizzaMagic)" : "");
    try
    {
        await DatabaseInitializer.InitializeAsync(
            db,
            Path.Combine(AppContext.BaseDirectory, "data"),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<PromotionRegistry>(),
            services.GetRequiredService<PricingMethodRegistry>(),
            database.Profile);
    }
    catch (DatabaseProfileException e)
    {
        Console.Error.WriteLine(e.Message);
        return 2;
    }
}

// Every error is a problem document (RFC 9457), including unknown routes and unexpected exceptions, which would
// otherwise be empty 404/500 responses; exception details are never written to the response.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
return 0;
