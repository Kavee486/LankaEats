using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Api.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");

        modelBuilder.Entity<MenuItem>(item =>
        {
            item.ToTable("menu_items");
            item.Property(i => i.Name).HasMaxLength(200);
            item.Property(i => i.Description).HasMaxLength(500);
            item.Property(i => i.Category).HasMaxLength(50);
            item.Property(i => i.Price).HasPrecision(12, 2);
            item.Property(i => i.Version).IsRowVersion();
            item.HasData(MenuSeed.Items);
        });

        modelBuilder.Entity<StockReservation>(r =>
        {
            r.ToTable("stock_reservations");
            r.HasKey(x => x.OrderId);
            r.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            r.Property(x => x.Reason).HasMaxLength(500);
            r.OwnsMany(x => x.Lines, l => l.ToJson());
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

internal sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql("Host=localhost;Database=inventory;Username=postgres;Password=postgres")
            .Options);
}
