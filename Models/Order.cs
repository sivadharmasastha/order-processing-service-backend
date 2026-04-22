using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrderProcessingSystem.Models
{
    public enum OrderStatus
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4,
        PartiallyProcessed = 5
    }

    public class Order
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string OrderNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string CustomerId { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string ProductName { get; set; } = string.Empty;

        [Required]
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }

        [Required]
        [Range(0.01, double.MaxValue)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [Required]
        public OrderStatus Status { get; set; } = OrderStatus.Pending;

        [Required]
        [StringLength(100)]
        public string IdempotencyKey { get; set; } = string.Empty;

        public int RetryCount { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? UpdatedAt { get; set; }
        
        public DateTime? ProcessedAt { get; set; }

        [StringLength(500)]
        public string? ErrorMessage { get; set; }

        public bool IsDeleted { get; set; } = false;

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        // Computed property
        [NotMapped]
        public bool IsProcessable => Status == OrderStatus.Pending || Status == OrderStatus.Failed;

        public void CalculateTotalAmount()
        {
            TotalAmount = Quantity * Price;
        }
    }

    public class IdempotencyKey
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Key { get; set; } = string.Empty;

        [Required]
        public int OrderId { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Processed";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        [StringLength(1000)]
        public string? ResponseData { get; set; }

        // Navigation property
        [ForeignKey(nameof(OrderId))]
        public Order? Order { get; set; }
    }

    public class OrderAuditLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        [StringLength(50)]
        public string Action { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string OldStatus { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string NewStatus { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Details { get; set; }

        [Required]
        [StringLength(100)]
        public string PerformedBy { get; set; } = "System";

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey(nameof(OrderId))]
        public Order? Order { get; set; }
    }
}
