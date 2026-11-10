using BookStore.Domain.Entities;

namespace BookStore.UnitTests.Domain;

public class OrderTests
{
    [Fact]
    public void Place_ComputesTotal_AndKeepsPurchasePrices()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        var order = Order.Place(Guid.NewGuid(), [(a, 2, 150.50m), (b, 1, 99m)]);

        Assert.Equal(400m, order.Total);
        Assert.All(order.Items, i => Assert.Equal(order.Id, i.OrderId));
        Assert.Equal(150.50m, order.Items.Single(i => i.BookId == a).Price);
    }

    [Fact]
    public void Place_WithoutLines_Throws() =>
        Assert.Throws<ArgumentException>(() => Order.Place(Guid.NewGuid(), []));

    [Fact]
    public void Place_WithZeroQuantity_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Order.Place(Guid.NewGuid(), [(Guid.NewGuid(), 0, 10m)]));
}
