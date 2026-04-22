namespace OrderProcessingSystem.Models
{
    /// <summary>
    /// Response model for order operations
    /// </summary>
    public class OrderResponse
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; }

        /// <summary>
        /// Create response from Order entity
        /// </summary>
        public static OrderResponse FromOrder(Order order)
        {
            return new OrderResponse
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                CustomerId = order.CustomerId,
                ProductName = order.ProductName,
                Quantity = order.Quantity,
                Price = order.Price,
                TotalAmount = order.TotalAmount,
                Status = order.Status.ToString(),
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                ProcessedAt = order.ProcessedAt,
                ErrorMessage = order.ErrorMessage,
                RetryCount = order.RetryCount
            };
        }
    }

    /// <summary>
    /// Paginated response wrapper
    /// </summary>
    public class PaginatedResponse<T>
    {
        public List<T> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }

    /// <summary>
    /// Standard API response wrapper
    /// </summary>
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
        public List<string>? Errors { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public static ApiResponse<T> SuccessResponse(T data, string? message = null)
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data,
                Message = message ?? "Request completed successfully"
            };
        }

        public static ApiResponse<T> ErrorResponse(string message, List<string>? errors = null)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Errors = errors
            };
        }
    }

    /// <summary>
    /// Bulk operation response
    /// </summary>
    public class BulkOrderResponse
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<OrderResponse> SuccessfulOrders { get; set; } = new();
        public List<BulkOrderError> Errors { get; set; } = new();
    }

    /// <summary>
    /// Error details for bulk operations
    /// </summary>
    public class BulkOrderError
    {
        public string IdempotencyKey { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int Index { get; set; }
    }

    /// <summary>
    /// Order statistics response
    /// </summary>
    public class OrderStatistics
    {
        public int TotalOrders { get; set; }
        public int PendingOrders { get; set; }
        public int ProcessingOrders { get; set; }
        public int CompletedOrders { get; set; }
        public int FailedOrders { get; set; }
        public int CancelledOrders { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal AverageOrderValue { get; set; }
        public DateTime? LastOrderDate { get; set; }
    }
}