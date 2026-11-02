using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Infrastructure.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Order> Orders => Set<Order>();

    Task IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orders");

        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders");
            order.HasKey(o => o.Id);
            order.Property(o => o.CustomerId).HasMaxLength(100).IsRequired();
            order.Property(o => o.DeliveryAddress).HasMaxLength(300).IsRequired();
            order.Property(o => o.PaymentMethod).HasMaxLength(40).IsRequired();
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
            order.Property(o => o.Total).HasPrecision(12, 2);
            order.Property(o => o.RejectionReason).HasMaxLength(500);
            order.HasIndex(o => new { o.CustomerId, o.CreatedAt });

            order.OwnsMany(o => o.Lines, line =>
            {
                line.ToTable("order_lines");
                line.WithOwner().HasForeignKey("OrderId");
                line.Property<int>("Id");
                line.HasKey("Id");
                line.Property(l => l.Name).HasMaxLength(200).IsRequired();
                line.Property(l => l.UnitPrice).HasPrecision(12, 2);
                line.Ignore(l => l.LineTotal);
            });
            order.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // Transactional outbox and inbox tables (MassTransit).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

/// <summary>Used by <c>dotnet ef</c> to create migrations without starting the whole service.</summary>
internal sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql("Host=localhost;Database=orders;Username=postgres;Password=postgres")
            .Options);
}
