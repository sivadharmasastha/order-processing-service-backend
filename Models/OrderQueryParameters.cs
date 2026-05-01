namespace OrderProcessingSystem.Models
{
    /// <summary>
    /// Query parameters for retrieving orders with filtering, sorting, and pagination
    /// </summary>
    public class OrderQueryParameters
    {
        private const int MaxPageSize = 100;
        private int _pageSize = 20;

        /// <summary>
        /// Page number (1-based)
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Number of items per page (max 100)
        /// </summary>
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = (value > MaxPageSize) ? MaxPageSize : value;
        }

        /// <summary>
        /// Filter by order status (Pending, Processing, Completed, Failed, Cancelled)
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Filter by customer ID
        /// </summary>
        public string? CustomerId { get; set; }

        /// <summary>
        /// Filter by product name (partial match)
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// Filter orders created from this date (UTC)
        /// </summary>
        public DateTime? FromDate { get; set; }

        /// <summary>
        /// Filter orders created until this date (UTC)
        /// </summary>
        public DateTime? ToDate { get; set; }

        /// <summary>
        /// Minimum order amount filter
        /// </summary>
        public decimal? MinAmount { get; set; }

        /// <summary>
        /// Maximum order amount filter
        /// </summary>
        public decimal? MaxAmount { get; set; }

        /// <summary>
        /// Search term for order number, customer ID, or product name
        /// </summary>
        public string? SearchTerm { get; set; }

        /// <summary>
        /// Sort field (OrderNumber, CreatedAt, TotalAmount, Status, CustomerId)
        /// </summary>
        public string SortBy { get; set; } = "CreatedAt";

        /// <summary>
        /// Sort direction (asc or desc)
        /// </summary>
        public string SortOrder { get; set; } = "desc";

        /// <summary>
        /// Include soft-deleted orders
        /// </summary>
        public bool IncludeDeleted { get; set; } = false;

        /// <summary>
        /// Calculate skip count for pagination
        /// </summary>
        public int CalculateSkip() => (PageNumber - 1) * PageSize;

        /// <summary>
        /// Validate the query parameters
        /// </summary>
        public void Validate()
        {
            if (PageNumber < 1)
                PageNumber = 1;

            if (PageSize < 1)
                PageSize = 1;

            if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate)
            {
                throw new ArgumentException("FromDate cannot be greater than ToDate");
            }

            if (MinAmount.HasValue && MaxAmount.HasValue && MinAmount > MaxAmount)
            {
                throw new ArgumentException("MinAmount cannot be greater than MaxAmount");
            }

            // Normalize sort order
            SortOrder = SortOrder?.ToLower() == "asc" ? "asc" : "desc";
        }
    }
}
