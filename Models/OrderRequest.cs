using System.ComponentModel.DataAnnotations;

namespace OrderProcessingSystem.Models
{
    /// <summary>
    /// Request model for creating a new order
    /// </summary>
    public class OrderRequest
    {
        /// <summary>
        /// Unique idempotency key to prevent duplicate orders
        /// </summary>
        [Required(ErrorMessage = "Idempotency key is required")]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Idempotency key must be between 10 and 100 characters")]
        public string IdempotencyKey { get; set; } = string.Empty;

        /// <summary>
        /// Customer identifier
        /// </summary>
        [Required(ErrorMessage = "Customer ID is required")]
        [StringLength(100, ErrorMessage = "Customer ID cannot exceed 100 characters")]
        public string CustomerId { get; set; } = string.Empty;

        /// <summary>
        /// Name of the product being ordered
        /// </summary>
        [Required(ErrorMessage = "Product name is required")]
        [StringLength(200, MinimumLength = 3, ErrorMessage = "Product name must be between 3 and 200 characters")]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// Quantity of items to order
        /// </summary>
        [Required(ErrorMessage = "Quantity is required")]
        [Range(1, 10000, ErrorMessage = "Quantity must be between 1 and 10,000")]
        public int Quantity { get; set; }

        /// <summary>
        /// Price per unit
        /// </summary>
        [Required(ErrorMessage = "Price is required")]
        [Range(0.01, 1000000, ErrorMessage = "Price must be between 0.01 and 1,000,000")]
        public decimal Price { get; set; }

        /// <summary>
        /// Optional customer notes
        /// </summary>
        [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters")]
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Request model for updating order status
    /// </summary>
    public class UpdateOrderStatusRequest
    {
        /// <summary>
        /// New status for the order
        /// </summary>
        [Required(ErrorMessage = "Status is required")]
        public OrderStatus Status { get; set; }

        /// <summary>
        /// Reason for status change
        /// </summary>
        [StringLength(500, ErrorMessage = "Reason cannot exceed 500 characters")]
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Request model for bulk order operations
    /// </summary>
    public class BulkOrderRequest
    {
        /// <summary>
        /// List of orders to process
        /// </summary>
        [Required]
        [MinLength(1, ErrorMessage = "At least one order is required")]
        [MaxLength(100, ErrorMessage = "Cannot process more than 100 orders at once")]
        public List<OrderRequest> Orders { get; set; } = new();
    }
}