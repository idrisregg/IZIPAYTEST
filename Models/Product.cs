namespace IZIPay.Models;

public sealed record Product(int Id, string Name, int Amount);

public static class ProductCatalog
{
    public static readonly IReadOnlyList<Product> Products =
    [
        new(1, "Starter", 1000),
        new(2, "Pro", 2500),
        new(3, "Business", 5000)
    ];

    public static Product? Find(int productId) => Products.FirstOrDefault(product => product.Id == productId);
}
