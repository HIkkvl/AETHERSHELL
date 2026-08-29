using AetherShell.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Data
{
    /// <summary>
    /// Платформенный уровень: реестр клубов, аккаунты владельцев, заявки с лендинга
    /// и посетители сетей. Живёт в одной общей базе. Операционные данные клубов
    /// сюда не попадают — у каждого клуба своя база, см. <see cref="ClubDbContext"/>.
    ///
    /// Посетители лежат здесь, а не в базе клуба, потому что баланс общий на сеть
    /// филиалов: изоляция идёт по сети, а не по отдельному залу.
    /// </summary>
    public class PlatformDbContext : DbContext
    {
        public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options) { }

        public DbSet<Account> Accounts { get; set; }
        public DbSet<Club> Clubs { get; set; }
        public DbSet<Lead> Leads { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<ClientGroup> ClientGroups { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Account>(e =>
            {
                e.HasIndex(a => a.Email).IsUnique();
                e.Property(a => a.Email).HasMaxLength(256).IsRequired();
                e.Property(a => a.Role).HasMaxLength(32).IsRequired();
            });

            modelBuilder.Entity<Club>(e =>
            {
                e.HasIndex(c => c.EnrollmentKey).IsUnique();
                e.HasIndex(c => c.Slug).IsUnique();
                e.Property(c => c.Name).HasMaxLength(200).IsRequired();
                e.Property(c => c.Slug).HasMaxLength(200).IsRequired();
                e.Property(c => c.EnrollmentKey).HasMaxLength(64).IsRequired();
                e.Property(c => c.LoyaltyFirstThreshold).HasColumnType("decimal(18,2)");
                e.Property(c => c.LoyaltyStep).HasColumnType("decimal(18,2)");

                e.HasOne(c => c.Owner)
                 .WithMany(a => a.Clubs)
                 .HasForeignKey(c => c.OwnerId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ClientGroup>(e =>
            {
                e.HasIndex(g => new { g.NetworkId, g.Name }).IsUnique();
                e.Property(g => g.Name).HasMaxLength(64).IsRequired();
                e.Property(g => g.Color).HasMaxLength(16).IsRequired();

                e.HasOne(g => g.Network)
                 .WithMany()
                 .HasForeignKey(g => g.NetworkId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Client>(e =>
            {
                // Логин уникален внутри сети, а не глобально: в разных сетях
                // спокойно может быть свой «player1».
                e.HasIndex(c => new { c.NetworkId, c.Username }).IsUnique();
                e.HasIndex(c => new { c.NetworkId, c.Email });
                e.HasIndex(c => c.GroupId);

                e.Property(c => c.Username).HasMaxLength(64).IsRequired();
                e.Property(c => c.Email).HasMaxLength(256);
                e.Property(c => c.Balance).HasColumnType("decimal(18,2)");
                e.Property(c => c.TotalSpent).HasColumnType("decimal(18,2)");

                e.HasOne(c => c.Network)
                 .WithMany()
                 .HasForeignKey(c => c.NetworkId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(c => c.Group)
                 .WithMany()
                 .HasForeignKey(c => c.GroupId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Lead>(e =>
            {
                e.Property(l => l.ClubName).HasMaxLength(200).IsRequired();
                e.HasOne(l => l.CreatedClub)
                 .WithMany()
                 .HasForeignKey(l => l.CreatedClubId)
                 .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
