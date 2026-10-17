using Fitzobit.Application.Customers;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Infrastructure.Persistence;

/// <summary>
/// "FITZ-000001", "FITZ-000002", … from a PostgreSQL sequence. nextval() never returns the same number twice, even for
/// concurrent requests or rolled-back transactions, so ids are unique without locking (gaps are possible and harmless).
/// Numbers already taken by an existing customer (an id kept from earlier data) are skipped.
/// </summary>
public sealed class SequenceCustomerIdGenerator(FitzobitDbContext db) : ICustomerIdGenerator
{
    public const string Prefix = "FITZ-";

    public static string Format(long number) => $"{Prefix}{number:000000}";

    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var number = await db.Database
                .SqlQueryRaw<long>($"SELECT nextval('\"{FitzobitDbContext.CustomerNumberSequence}\"') AS \"Value\"")
                .SingleAsync(ct);
            var id = Format(number);
            if (!await db.Customers.AnyAsync(c => c.CustomerId == id, ct)) return id;
        }
    }
}
