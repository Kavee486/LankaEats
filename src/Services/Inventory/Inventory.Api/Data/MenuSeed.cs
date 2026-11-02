namespace Inventory.Api.Data;

/// <summary>Sri Lankan menu used as seed data. The ids are fixed so migrations stay deterministic.</summary>
public static class MenuSeed
{
    public static readonly Guid ChickenKottu = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    public static readonly Guid Lamprais = Guid.Parse("0199a000-0000-7000-8000-000000000002");
    public static readonly Guid EggHoppers = Guid.Parse("0199a000-0000-7000-8000-000000000003");
    public static readonly Guid StringHoppers = Guid.Parse("0199a000-0000-7000-8000-000000000004");
    public static readonly Guid RiceAndCurry = Guid.Parse("0199a000-0000-7000-8000-000000000005");
    public static readonly Guid FishAmbulThiyal = Guid.Parse("0199a000-0000-7000-8000-000000000006");
    public static readonly Guid Watalappan = Guid.Parse("0199a000-0000-7000-8000-000000000007");
    public static readonly Guid FaludaDrink = Guid.Parse("0199a000-0000-7000-8000-000000000008");

    public static MenuItem[] Items =>
    [
        new() { Id = ChickenKottu, Name = "Chicken Kottu", Category = "Mains", Price = 1450m, AvailableQuantity = 100,
            Description = "Chopped godamba roti stir-fried with chicken, egg, leeks and curry gravy." },
        new() { Id = Lamprais, Name = "Lamprais", Category = "Mains", Price = 2200m, AvailableQuantity = 30,
            Description = "Dutch-Burgher rice parcel baked in banana leaf with frikkadels and blachan." },
        new() { Id = EggHoppers, Name = "Egg Hoppers (2)", Category = "Mains", Price = 650m, AvailableQuantity = 80,
            Description = "Crispy rice-flour appa with a soft egg, served with lunu miris." },
        new() { Id = StringHoppers, Name = "String Hoppers (10)", Category = "Mains", Price = 750m, AvailableQuantity = 60,
            Description = "Steamed idiyappam with kiri hodi and pol sambol." },
        new() { Id = RiceAndCurry, Name = "Rice & Curry", Category = "Mains", Price = 1100m, AvailableQuantity = 120,
            Description = "Red rice with dhal, polos, gotukola mallum and a chicken curry." },
        new() { Id = FishAmbulThiyal, Name = "Fish Ambul Thiyal", Category = "Mains", Price = 1850m, AvailableQuantity = 5,
            Description = "Southern sour tuna curry with goraka. Limited daily stock." },
        new() { Id = Watalappan, Name = "Watalappan", Category = "Desserts", Price = 550m, AvailableQuantity = 40,
            Description = "Kithul jaggery and coconut custard with cashew." },
        new() { Id = FaludaDrink, Name = "Faluda", Category = "Drinks", Price = 600m, AvailableQuantity = 50,
            Description = "Rose syrup, basil seeds, jelly and ice cream." },
    ];
}
