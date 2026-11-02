using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payments.Api.Data;

public enum PaymentStatus
{
    Succeeded,
    Failed,
}

public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public required string CustomerId { get; set; }
    public decimal Amount { get; set; }
    public required string Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");

        modelBuilder.Entity<Payment>(p =>
        {
            p.ToTable("payments");
            // One charge per order, enforced by the database. This is the last line of defence against double charging.
            p.HasIndex(x => x.OrderId).IsUnique();
            p.Property(x => x.CustomerId).HasMaxLength(100);
            p.Property(x => x.Amount).HasPrecision(12, 2);
            p.Property(x => x.Method).HasMaxLength(40);
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.ProviderReference).HasMaxLength(100);
            p.Property(x => x.FailureReason).HasMaxLength(500);
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

internal sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql("Host=localhost;Database=payments;Username=postgres;Password=postgres")
            .Options);
}
