using Microsoft.AspNetCore.DataProtection;

namespace PizzaMagic.Web.Models.Orders;

/// <summary>The customer's recent order ids (newest first, at most 10, 7 days) in a data-protected cookie.</summary>
#pragma warning disable CS9113 // M14 red stub
public sealed class RecentOrdersCookie(IDataProtectionProvider provider)
#pragma warning restore CS9113
{
    public IReadOnlyList<Guid> Read(HttpRequest request) => throw new NotImplementedException();

    public void Add(HttpContext context, Guid orderId) => throw new NotImplementedException();
}
