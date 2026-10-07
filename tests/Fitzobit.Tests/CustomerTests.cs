using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

public class CustomerTests
{
    private static Customer NewCustomer(string customerId, string phone = "9876543210")
    {
        var customer = new Customer { CustomerId = customerId, CustomerName = "Rahul Sharma", Pincode = "600040" };
        customer.SetPhoneNumber(phone);
        customer.SetLocation("Anna Nagar, Chennai");
        return customer;
    }

    [Fact]
    public async Task CustomerId_must_be_unique()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            db.Customers.Add(NewCustomer("CUST001"));
            await db.SaveChangesAsync();
        }

        await using (var db = test.CreateContext())
        {
            db.Customers.Add(NewCustomer("CUST001", "9123456789"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = test.CreateContext())
            Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task Saving_stamps_audit_columns()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var customer = NewCustomer("CUST002");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        Assert.Equal(test.Clock.UtcNow, customer.CreatedAt);
        Assert.Equal(test.User.UserName, customer.CreatedBy);
        Assert.Equal("9876543210", customer.NormalizedPhoneNumber);
        Assert.Equal("anna nagar, chennai", customer.NormalizedLocation);
    }

    [Theory]
    [InlineData("+91 98765 43210", "9876543210")]
    [InlineData("09876543210", "9876543210")]
    [InlineData("98765-43210", "9876543210")]
    [InlineData("", null)]
    public void Phone_numbers_are_normalised(string input, string? expected) =>
        Assert.Equal(expected, Normalization.NormalizePhone(input));

    [Fact]
    public void Locations_are_normalised_for_matching() =>
        Assert.Equal(Normalization.NormalizeLocation("rs puram, coimbatore"), Normalization.NormalizeLocation("  RS Puram ,   Coimbatore "));
}
