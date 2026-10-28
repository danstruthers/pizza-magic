using PizzaMagic.Web.Clients;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
// The only way to the order service (plan section 6.5); the order builder never touches the database.
builder.Services.AddOrderManagerClient(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
