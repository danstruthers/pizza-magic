namespace PizzaMagic.Domain.Promotions;

/// <summary>
/// A customer-facing message whose money amounts are placeholders like <c>{subtotal}</c>, with the amounts
/// in cents. Domain never formats money; each host renders the message with its money formatter.
/// </summary>
public sealed record PromoMessage(string Template, IReadOnlyDictionary<string, long> Amounts)
{
    public static PromoMessage Plain(string text) => new(text, new Dictionary<string, long>());

    public string Render(Func<long, string> formatMoney) =>
        Amounts.Aggregate(Template, (text, amount) => text.Replace("{" + amount.Key + "}", formatMoney(amount.Value)));

    /// <summary>Joins several messages, renaming placeholders so they cannot collide.</summary>
    public static PromoMessage Combine(IReadOnlyList<PromoMessage> messages)
    {
        if (messages.Count == 1)
        {
            return messages[0];
        }

        var amounts = new Dictionary<string, long>();
        var parts = new List<string>();
        for (var i = 0; i < messages.Count; i++)
        {
            var template = messages[i].Template;
            foreach (var (key, value) in messages[i].Amounts)
            {
                template = template.Replace("{" + key + "}", "{" + i + "." + key + "}");
                amounts[i + "." + key] = value;
            }
            parts.Add(template);
        }
        return new PromoMessage(string.Join(" ", parts), amounts);
    }
}
