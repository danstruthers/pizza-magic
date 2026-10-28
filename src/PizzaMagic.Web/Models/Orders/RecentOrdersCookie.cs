using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace PizzaMagic.Web.Models.Orders;

/// <summary>The customer's recent order ids (newest first, at most 10, 7 days) in a data-protected cookie.</summary>
public sealed class RecentOrdersCookie(IDataProtectionProvider provider)
{
    public const string CookieName = "pm_recent";
    private const int MaxOrders = 10;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly IDataProtector protector = provider.CreateProtector("PizzaMagic.Web.RecentOrders.v1");

    /// <summary>The saved ids; empty when there is no cookie or it can't be read (altered, or keys rotated).</summary>
    public IReadOnlyList<Guid> Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return [];
        }
        try
        {
            return protector.Unprotect(value).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return [];
        }
    }

    public void Add(HttpContext context, Guid orderId)
    {
        var ids = Read(context.Request).Where(id => id != orderId).Prepend(orderId).Take(MaxOrders);
        context.Response.Cookies.Append(CookieName, protector.Protect(string.Join(',', ids)), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Secure = context.Request.IsHttps,
            MaxAge = Lifetime,
        });
    }
}
