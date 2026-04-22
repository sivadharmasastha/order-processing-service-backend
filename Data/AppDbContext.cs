using Microsoft.EntityFrameworkCore;
using OrderProcessingSystem.Models;

namespace OrderProcessingSystem.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Order> Orders { get; set; }
        public DbSet<IdempotencyKey> IdempotencyKeys { get; set; }
        public DbSet<OrderAuditLog> OrderAuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Global query filter for soft delete
            modelBuilder.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);

            // Configure Order entity
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("Orders");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id)
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.OrderNumber)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.CustomerId)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.ProductName)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.Quantity)
                    .IsRequired();

                entity.Property(e => e.Price)
                    .IsRequired()
                    .HasColumnType("decimal(18,2)");

                entity.Property(e => e.TotalAmount)
                    .IsRequired()
                    .HasColumnType("decimal(18,2)");

                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasConversion<string>()
                    .HasDefaultValue(OrderStatus.Pending);

                entity.Property(e => e.IdempotencyKey)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.RetryCount)
                    .HasDefaultValue(0);

                entity.Property(e => e.CreatedAt)
                    .IsRequired()
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.Property(e => e.UpdatedAt);

                entity.Property(e => e.ProcessedAt);

                entity.Property(e => e.ErrorMessage)
                    .HasMaxLength(500);

                entity.Property(e => e.IsDeleted)
                    .IsRequired()
                    .HasDefaultValue(false);

                entity.Property(e => e.RowVersion)
                    .IsRowVersion();

                // Unique constraints
                entity.HasIndex(e => e.OrderNumber)
                    .IsUnique();

                entity.HasIndex(e => e.IdempotencyKey)
                    .IsUnique();

                // Performance indexes
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.CustomerId);
                entity.HasIndex(e => new { e.Status, e.CreatedAt });
                entity.HasIndex(e => new { e.CustomerId, e.CreatedAt });
            });

            // Configure IdempotencyKey entity
            modelBuilder.Entity<IdempotencyKey>(entity =>
            {
                entity.ToTable("IdempotencyKeys");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Key)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.OrderId)
                    .IsRequired();

                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasMaxLength(50)
                    .HasDefaultValue("Processed");

                entity.Property(e => e.CreatedAt)
                    .IsRequired()
                    .HasDefaultValueSql("GETUTCDATE()");

                entity.Property(e => e.ExpiresAt)
                    .IsRequired();

                entity.Property(e => e.ResponseData)
                    .HasMaxLength(1000);

                // Unique constraint on Key
                entity.HasIndex(e => e.Key)
                    .IsUnique();

                // Index for cleanup queries
                entity.HasIndex(e => e.ExpiresAt);

                // Foreign key relationship
                entity.HasOne(e => e.Order)
                    .WithMany()
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure OrderAuditLog entity
            modelBuilder.Entity<OrderAuditLog>(entity =>
            {
                entity.ToTable("OrderAuditLogs");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.OrderId)
                    .IsRequired();

                entity.Property(e => e.Action)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.OldStatus)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.NewStatus)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.Details)
                    .HasMaxLength(500);

                entity.Property(e => e.PerformedBy)
                    .IsRequired()
                    .HasMaxLength(100)
                    .HasDefaultValue("System");

                entity.Property(e => e.Timestamp)
                    .IsRequired()
                    .HasDefaultValueSql("GETUTCDATE()");

                // Performance indexes
                entity.HasIndex(e => e.OrderId);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => new { e.OrderId, e.Timestamp });

                // Foreign key relationship
                entity.HasOne(e => e.Order)
                    .WithMany()
                    .HasForeignKey(e => e.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Auto-update UpdatedAt timestamp
            var entries = ChangeTracker.Entries<Order>()
                .Where(e => e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
