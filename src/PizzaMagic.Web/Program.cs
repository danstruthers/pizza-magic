using PizzaMagic.Web.Clients;
using PizzaMagic.Web.Models.Checkout;
using PizzaMagic.Web.Models.Orders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
// The only way to the order service (plan section 6.5); the order builder never touches the database.
builder.Services.AddOrderManagerClient(builder.Configuration);
// The cart lives in the Session (plan section 6.2); the builder posts JSON with the antiforgery token in a header.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.IsEssential = true;
});
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
// Checkout seals what Review showed into a ticket; recent orders live in a cookie (plan section 6.2).
builder.Services.AddSingleton<ReviewTicketProtector>();
builder.Services.AddSingleton<RecentOrdersCookie>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Builder}/{action=Index}/{id?}");

app.Run();
