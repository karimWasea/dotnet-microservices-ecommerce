using Microsoft.EntityFrameworkCore;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.Infrastructure.Persistence;

public static class ProductDbSeeder
{
    public static async Task MigrateAndSeedAsync(
        ProductDbContext dbContext,
        IProductRepository repository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        bool seedSampleData = true,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        if (!seedSampleData)
        {
            return;
        }

        if (await dbContext.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var seeds = new (string Name, string Description, string Sku, decimal Price, string Category)[]
        {
            ("Mechanical Keyboard Pro", "Wireless RGB mechanical keyboard with tactile switches", "KB-MECH-001", 129.99m, "Peripherals"),
            ("Ergonomic Vertical Mouse", "Bluetooth vertical mouse designed to reduce wrist strain", "MS-ERGO-002", 59.50m, "Peripherals"),
            ("UltraWide 34-inch Monitor", "34-inch WQHD curved IPS monitor, 144Hz refresh rate", "MN-UW34-003", 499.00m, "Monitors"),
            ("4K Studio Display 27-inch", "27-inch 4K UHD color-accurate monitor with USB-C hub", "MN-4K27-004", 389.99m, "Monitors"),
            ("Noise-Cancelling Headphones", "Over-ear wireless headphones with active noise cancellation", "AU-ANC-005", 249.00m, "Audio"),
            ("USB Condenser Microphone", "Cardioid studio USB-C microphone with desktop stand", "AU-MIC-006", 99.95m, "Audio"),
            ("Studio Monitor Speakers", "Pair of 5-inch powered bookshelf studio monitors", "AU-SPK-007", 199.00m, "Audio"),
            ("Thunderbolt 4 Docking Station", "15-in-1 docking station with 96W power delivery and dual 4K", "AC-DOCK-008", 219.00m, "Accessories"),
            ("Aluminum Laptop Stand", "Foldable ventilated aluminum stand for 13 to 17-inch laptops", "AC-STND-009", 34.99m, "Accessories"),
            ("100W GaN USB-C Charger", "Compact 4-port GaN fast wall charger for laptops and phones", "AC-CHRG-010", 64.99m, "Accessories"),
            ("NVMe M.2 SSD 1TB", "PCIe Gen4 x4 NVMe internal solid state drive up to 7000 MB/s", "ST-NVME-011", 89.99m, "Storage"),
            ("NVMe M.2 SSD 2TB", "High-capacity PCIe Gen4 x4 NVMe internal SSD with heatsink", "ST-NVME-012", 159.99m, "Storage"),
            ("Portable Rugged SSD 1TB", "IP65 water and dust resistant USB 3.2 Gen2 external SSD", "ST-PORT-013", 109.50m, "Storage"),
            ("Wi-Fi 7 Mesh Router", "Tri-band Wi-Fi 7 router with 2.5G WAN/LAN ports", "NW-WIFI-014", 279.00m, "Networking"),
            ("8-Port 2.5G Unmanaged Switch", "Fanless metal desktop Ethernet switch with 8x 2.5GbE ports", "NW-SW08-015", 119.00m, "Networking"),
            ("1080p 60fps Webcam", "Full HD streaming webcam with built-in privacy shutter", "CM-WEB-016", 69.99m, "Cameras"),
            ("4K AI Auto-Framing Webcam", "Ultra HD webcam with Sony sensor and HDR support", "CM-WEB-017", 149.00m, "Cameras"),
            ("XL Extended Desk Mat", "Water-resistant stitched-edge desk pad (900x400mm)", "AC-MAT-018", 24.99m, "Accessories"),
            ("Dual Monitor Arm", "Gas-spring desk mount for two 17 to 32-inch displays", "AC-ARM-019", 79.99m, "Accessories"),
            ("Smart UPS Battery Backup", "1500VA/900W sine wave UPS with LCD display", "PW-UPS-020", 229.99m, "Power")
        };

        foreach (var item in seeds)
        {
            var product = Product.Create(
                item.Name,
                item.Description,
                Sku.Create(item.Sku),
                Price.Create(item.Price),
                item.Category,
                now);

            repository.Add(product);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
