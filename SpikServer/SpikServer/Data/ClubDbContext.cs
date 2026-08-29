using AetherShell.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Data
{
    /// <summary>
    /// Данные одного клуба. У каждого клуба своя база (aether_club_{id}), поэтому
    /// колонки ClubId и глобальные query-фильтры больше не нужны: данные разных
    /// клубов физически не могут пересечься даже при ошибке в запросе.
    ///
    /// К какой именно базе подключаться, решает <see cref="IClubDbConnectionFactory"/>
    /// по клубу текущего запроса.
    ///
    /// Посетителей здесь нет: их баланс общий на сеть филиалов, поэтому они лежат
    /// в платформенной базе (<see cref="Models.Client"/>). В <see cref="Users"/>
    /// остаётся только персонал зала.
    /// </summary>
    public class ClubDbContext : DbContext
    {
        public ClubDbContext(DbContextOptions<ClubDbContext> options) : base(options) { }

        public DbSet<Computer> Computers { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<AppItem> AppItems { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<StaffShift> StaffShifts { get; set; }
        public DbSet<Tariff> Tariffs { get; set; }
        public DbSet<AdminLog> AdminLogs { get; set; }

        public DbSet<Product> Products { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<Banner> Banners { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Computer>(e =>
            {
                e.HasIndex(c => c.Name).IsUnique();
                e.HasIndex(c => c.HardwareId).IsUnique();
            });

            modelBuilder.Entity<User>(e =>
            {
                e.HasIndex(u => u.Username).IsUnique();
                e.HasIndex(u => u.Email);
            });

            modelBuilder.Entity<StaffShift>(e =>
            {
                e.HasIndex(s => new { s.UserId, s.EndedAt });
                e.HasIndex(s => s.StartedAt);
                e.Property(s => s.Username).HasMaxLength(64);
            });

            modelBuilder.Entity<Session>(e =>
            {
                e.HasIndex(s => new { s.ComputerName, s.IsActive });
                e.HasIndex(s => s.Username);
            });

            modelBuilder.Entity<ChatMessage>(e =>
            {
                e.HasIndex(c => new { c.PcName, c.CreatedAt });
            });

            modelBuilder.Entity<Order>(e =>
            {
                // Покупатель живёт в платформенной базе, внешний ключ поставить некуда.
                e.Property(o => o.Username).HasMaxLength(64);
                e.HasIndex(o => o.Username);
                e.HasIndex(o => o.Status);
            });

            modelBuilder.Entity<OrderItem>(e =>
            {
                e.HasOne(i => i.Order)
                 .WithMany(o => o.Items)
                 .HasForeignKey(i => i.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(i => i.Product)
                 .WithMany()
                 .HasForeignKey(i => i.ProductId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StockMovement>(e =>
            {
                e.HasOne(m => m.Product)
                 .WithMany(p => p.Movements)
                 .HasForeignKey(m => m.ProductId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(m => new { m.ProductId, m.CreatedAt });
                e.HasIndex(m => m.CreatedAt);
            });

            modelBuilder.Entity<Tariff>(e =>
            {
                e.Property(t => t.Price).HasColumnType("decimal(18,2)");
            });
        }
    }
}
