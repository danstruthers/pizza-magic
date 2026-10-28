namespace PizzaMagic.Web.Clients;

/// <summary>The restaurant this order builder sells for (config OrderManager:RestaurantId; empty = the service's default).</summary>
public static class ConfiguredRestaurant
{
    public static long? Id(IConfiguration configuration) =>
        long.TryParse(configuration["OrderManager:RestaurantId"], out var id) ? id : null;
}
