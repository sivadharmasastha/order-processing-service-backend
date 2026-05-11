using Microsoft.EntityFrameworkCore;
using OrderProcessingSystem.Data;
using OrderProcessingSystem.Models;

namespace OrderProcessingSystem.Repositories
{
    /// <summary>
    /// Interface for Order repository operations
    /// </summary>
    public interface IOrderRepository
    {
        Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default);
        Task<IEnumerable<Order>> GetAllOrdersAsync(CancellationToken cancellationToken = default);
        Task<(IEnumerable<Order> Orders, int TotalCount)> GetOrdersWithFilteringAsync(
            OrderQueryParameters parameters, 
            CancellationToken cancellationToken = default);
        Task<Order?> GetOrderByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<Order?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
        Task<bool> UpdateOrderAsync(Order order, CancellationToken cancellationToken = default);
        Task<bool> DeleteOrderAsync(int id, CancellationToken cancellationToken = default);
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Repository implementation for Order entity operations
    /// </summary>
    public class OrderRepository : IOrderRepository
    {
        private readonly AppDbContext _context;
        private readonly ILogger<OrderRepository> _logger;

        public OrderRepository(AppDbContext context, ILogger<OrderRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new order in the database
        /// </summary>
        /// <param name="order">The order entity to create</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The created order with generated ID</returns>
        /// <exception cref="ArgumentNullException">Thrown when order is null</exception>
        /// <exception cref="DbUpdateException">Thrown when database update fails</exception>
        public async Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order), "Order cannot be null");
            }

            try
            {
                _logger.LogInformation("Creating order with OrderNumber: {OrderNumber}", order.OrderNumber);

                // Set timestamps
                order.CreatedAt = DateTime.UtcNow;
                order.UpdatedAt = null;
                order.IsDeleted = false;

                // Calculate total amount if not already set
                if (order.TotalAmount == 0)
                {
                    order.CalculateTotalAmount();
                }

                // Add order to context
                await _context.Orders.AddAsync(order, cancellationToken);

                // Save changes to database
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Successfully created order with ID: {OrderId}, OrderNumber: {OrderNumber}", 
                    order.Id, order.OrderNumber);

                return order;
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error while creating order with OrderNumber: {OrderNumber}", 
                    order.OrderNumber);
                throw new InvalidOperationException($"Failed to create order: {dbEx.Message}", dbEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating order with OrderNumber: {OrderNumber}", 
                    order.OrderNumber);
                throw;
            }
        }

        /// <summary>
        /// Retrieves all orders from the database (excluding soft-deleted orders)
        /// WARNING: Use GetOrdersWithFilteringAsync for production with pagination
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of all orders</returns>
        public async Task<IEnumerable<Order>> GetAllOrdersAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("Retrieving all orders from database");

                var orders = await _context.Orders
                    .AsNoTracking() // Use AsNoTracking for read-only operations to improve performance
                    .Where(o => !o.IsDeleted) // Filter out soft-deleted orders
                    .OrderByDescending(o => o.CreatedAt) // Most recent orders first
                    .ToListAsync(cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Successfully retrieved {OrderCount} orders ({ElapsedMs}ms)", 
                    orders.Count, stopwatch.ElapsedMilliseconds);

                return orders;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error occurred while retrieving all orders ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException("Failed to retrieve orders from database", ex);
            }
        }

        /// <summary>
        /// Retrieves an order by its ID
        /// </summary>
        /// <param name="id">The order ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The order if found, otherwise null</returns>
        public async Task<Order?> GetOrderByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogDebug("Retrieving order with ID: {OrderId}", id);

                var order = await _context.Orders
                    .AsNoTracking()
                    .Where(o => o.Id == id && !o.IsDeleted)
                    .FirstOrDefaultAsync(cancellationToken);

                stopwatch.Stop();
                if (order == null)
                {
                    _logger.LogWarning("Order with ID: {OrderId} not found ({ElapsedMs}ms)", id, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogDebug("Successfully retrieved order with ID: {OrderId} ({ElapsedMs}ms)", id, stopwatch.ElapsedMilliseconds);
                }

                return order;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error occurred while retrieving order with ID: {OrderId} ({ElapsedMs}ms)", id, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"Failed to retrieve order with ID: {id}", ex);
            }
        }

        /// <summary>
        /// Retrieves an order by its order number
        /// </summary>
        /// <param name="orderNumber">The order number</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The order if found, otherwise null</returns>
        public async Task<Order?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                throw new ArgumentException("Order number cannot be null or empty", nameof(orderNumber));
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogDebug("Retrieving order with OrderNumber: {OrderNumber}", orderNumber);

                var order = await _context.Orders
                    .AsNoTracking()
                    .Where(o => o.OrderNumber == orderNumber && !o.IsDeleted)
                    .FirstOrDefaultAsync(cancellationToken);

                stopwatch.Stop();
                if (order == null)
                {
                    _logger.LogWarning("Order with OrderNumber: {OrderNumber} not found ({ElapsedMs}ms)", orderNumber, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogDebug("Successfully retrieved order with OrderNumber: {OrderNumber} ({ElapsedMs}ms)", orderNumber, stopwatch.ElapsedMilliseconds);
                }

                return order;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error occurred while retrieving order with OrderNumber: {OrderNumber} ({ElapsedMs}ms)", orderNumber, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"Failed to retrieve order with OrderNumber: {orderNumber}", ex);
            }
        }

        /// <summary>
        /// Updates an existing order in the database
        /// </summary>
        /// <param name="order">The order entity with updated values</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if update was successful, false otherwise</returns>
        public async Task<bool> UpdateOrderAsync(Order order, CancellationToken cancellationToken = default)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order), "Order cannot be null");
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("Updating order with ID: {OrderId}", order.Id);

                // Update timestamp
                order.UpdatedAt = DateTime.UtcNow;

                // Recalculate total amount if quantity or price changed
                order.CalculateTotalAmount();

                _context.Orders.Update(order);
                var rowsAffected = await _context.SaveChangesAsync(cancellationToken);

                stopwatch.Stop();
                if (rowsAffected > 0)
                {
                    _logger.LogInformation(
                        "Successfully updated order with ID: {OrderId} ({ElapsedMs}ms)", 
                        order.Id, stopwatch.ElapsedMilliseconds);
                    return true;
                }

                _logger.LogWarning(
                    "No rows were affected when updating order with ID: {OrderId} ({ElapsedMs}ms)", 
                    order.Id, stopwatch.ElapsedMilliseconds);
                return false;
            }
            catch (DbUpdateConcurrencyException concurrencyEx)
            {
                stopwatch.Stop();
                _logger.LogError(
                    concurrencyEx, 
                    "Concurrency conflict while updating order with ID: {OrderId} ({ElapsedMs}ms)", 
                    order.Id, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"Concurrency conflict occurred while updating order with ID: {order.Id}", concurrencyEx);
            }
            catch (DbUpdateException dbEx)
            {
                stopwatch.Stop();
                _logger.LogError(
                    dbEx, 
                    "Database error while updating order with ID: {OrderId} ({ElapsedMs}ms)", 
                    order.Id, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"Failed to update order: {dbEx.Message}", dbEx);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex, 
                    "Unexpected error while updating order with ID: {OrderId} ({ElapsedMs}ms)", 
                    order.Id, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Soft deletes an order by marking it as deleted
        /// </summary>
        /// <param name="id">The order ID to delete</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if deletion was successful, false otherwise</returns>
        public async Task<bool> DeleteOrderAsync(int id, CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("Attempting to delete order with ID: {OrderId}", id);

                var order = await _context.Orders.FindAsync(new object[] { id }, cancellationToken);

                if (order == null)
                {
                    stopwatch.Stop();
                    _logger.LogWarning(
                        "Order with ID: {OrderId} not found for deletion ({ElapsedMs}ms)", 
                        id, stopwatch.ElapsedMilliseconds);
                    return false;
                }

                // Soft delete
                order.IsDeleted = true;
                order.UpdatedAt = DateTime.UtcNow;

                _context.Orders.Update(order);
                var rowsAffected = await _context.SaveChangesAsync(cancellationToken);

                stopwatch.Stop();
                if (rowsAffected > 0)
                {
                    _logger.LogInformation(
                        "Successfully deleted order with ID: {OrderId} ({ElapsedMs}ms)", 
                        id, stopwatch.ElapsedMilliseconds);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex, 
                    "Error occurred while deleting order with ID: {OrderId} ({ElapsedMs}ms)", 
                    id, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"Failed to delete order with ID: {id}", ex);
            }
        }

        /// <summary>
        /// Retrieves orders with advanced filtering, sorting, and pagination
        /// Optimized for production with efficient query execution
        /// </summary>
        /// <param name="parameters">Query parameters for filtering and pagination</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple containing filtered orders and total count</returns>
        public async Task<(IEnumerable<Order> Orders, int TotalCount)> GetOrdersWithFilteringAsync(
            OrderQueryParameters parameters, 
            CancellationToken cancellationToken = default)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters), "Query parameters cannot be null");
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogInformation(
                    "Retrieving orders with filters - Page: {PageNumber}, PageSize: {PageSize}, Status: {Status}, CustomerId: {CustomerId}, SearchTerm: {SearchTerm}",
                    parameters.PageNumber, parameters.PageSize, parameters.Status ?? "All", parameters.CustomerId ?? "All", parameters.SearchTerm ?? "None");

                // Validate parameters
                parameters.Validate();

                // Start with base query with AsNoTracking for better performance
                IQueryable<Order> query = _context.Orders
                    .AsNoTracking()
                    .AsSplitQuery(); // Use split query for better performance with complex filters

                // Apply soft-delete filter (exclude deleted by default)
                if (!parameters.IncludeDeleted)
                {
                    query = query.Where(o => !o.IsDeleted);
                }

                // Apply status filter
                if (!string.IsNullOrWhiteSpace(parameters.Status))
                {
                    if (Enum.TryParse<OrderStatus>(parameters.Status, true, out var status))
                    {
                        query = query.Where(o => o.Status == status);
                        _logger.LogDebug("Applied status filter: {Status}", status);
                    }
                }

                // Apply customer ID filter (exact match for better index usage)
                if (!string.IsNullOrWhiteSpace(parameters.CustomerId))
                {
                    query = query.Where(o => o.CustomerId == parameters.CustomerId);
                    _logger.LogDebug("Applied customer ID filter: {CustomerId}", parameters.CustomerId);
                }

                // Apply product name filter (partial match, case-insensitive)
                if (!string.IsNullOrWhiteSpace(parameters.ProductName))
                {
                    var productNameLower = parameters.ProductName.ToLower();
                    query = query.Where(o => o.ProductName.ToLower().Contains(productNameLower));
                    _logger.LogDebug("Applied product name filter: {ProductName}", parameters.ProductName);
                }

                // Apply date range filter (optimized with indexed CreatedAt)
                if (parameters.FromDate.HasValue)
                {
                    var fromDate = parameters.FromDate.Value.Date;
                    query = query.Where(o => o.CreatedAt >= fromDate);
                    _logger.LogDebug("Applied from date filter: {FromDate}", fromDate);
                }

                if (parameters.ToDate.HasValue)
                {
                    // Add one day to include the entire ToDate day
                    var toDateInclusive = parameters.ToDate.Value.Date.AddDays(1);
                    query = query.Where(o => o.CreatedAt < toDateInclusive);
                    _logger.LogDebug("Applied to date filter: {ToDate}", parameters.ToDate.Value.Date);
                }

                // Apply amount range filter (optimized with indexed TotalAmount)
                if (parameters.MinAmount.HasValue)
                {
                    query = query.Where(o => o.TotalAmount >= parameters.MinAmount.Value);
                    _logger.LogDebug("Applied min amount filter: {MinAmount}", parameters.MinAmount.Value);
                }

                if (parameters.MaxAmount.HasValue)
                {
                    query = query.Where(o => o.TotalAmount <= parameters.MaxAmount.Value);
                    _logger.LogDebug("Applied max amount filter: {MaxAmount}", parameters.MaxAmount.Value);
                }

                // Apply search term (searches across order number, customer ID, and product name)
                // Using OR conditions - consider full-text search for production at scale
                if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
                {
                    var searchTermLower = parameters.SearchTerm.Trim().ToLower();
                    query = query.Where(o =>
                        o.OrderNumber.ToLower().Contains(searchTermLower) ||
                        o.CustomerId.ToLower().Contains(searchTermLower) ||
                        o.ProductName.ToLower().Contains(searchTermLower));
                    _logger.LogDebug("Applied search term filter: {SearchTerm}", parameters.SearchTerm);
                }

                // Get total count before pagination (executed as separate query)
                var countStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var totalCount = await query.CountAsync(cancellationToken);
                countStopwatch.Stop();
                _logger.LogDebug("Count query executed in {ElapsedMs}ms, Total: {TotalCount}", countStopwatch.ElapsedMilliseconds, totalCount);

                // Apply sorting (use indexes where possible)
                query = parameters.SortBy?.ToLower() switch
                {
                    "ordernumber" => parameters.SortOrder == "asc" 
                        ? query.OrderBy(o => o.OrderNumber) 
                        : query.OrderByDescending(o => o.OrderNumber),
                    "customerid" => parameters.SortOrder == "asc" 
                        ? query.OrderBy(o => o.CustomerId) 
                        : query.OrderByDescending(o => o.CustomerId),
                    "totalamount" => parameters.SortOrder == "asc" 
                        ? query.OrderBy(o => o.TotalAmount) 
                        : query.OrderByDescending(o => o.TotalAmount),
                    "status" => parameters.SortOrder == "asc" 
                        ? query.OrderBy(o => o.Status) 
                        : query.OrderByDescending(o => o.Status),
                    "productname" => parameters.SortOrder == "asc" 
                        ? query.OrderBy(o => o.ProductName) 
                        : query.OrderByDescending(o => o.ProductName),
                    "createdat" or _ => parameters.SortOrder == "asc" 
                        ? query.OrderBy(o => o.CreatedAt) 
                        : query.OrderByDescending(o => o.CreatedAt)
                };

                // Apply pagination (Skip/Take for efficient paging)
                var skip = parameters.CalculateSkip();
                var dataStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var orders = await query
                    .Skip(skip)
                    .Take(parameters.PageSize)
                    .ToListAsync(cancellationToken);
                dataStopwatch.Stop();

                stopwatch.Stop();
                _logger.LogInformation(
                    "Successfully retrieved {OrderCount} orders out of {TotalCount} total (Page {PageNumber}, Query: {QueryMs}ms, Count: {CountMs}ms, Data: {DataMs}ms, Total: {TotalMs}ms)",
                    orders.Count, totalCount, parameters.PageNumber, 
                    stopwatch.ElapsedMilliseconds - countStopwatch.ElapsedMilliseconds - dataStopwatch.ElapsedMilliseconds,
                    countStopwatch.ElapsedMilliseconds, dataStopwatch.ElapsedMilliseconds, stopwatch.ElapsedMilliseconds);

                return (orders, totalCount);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error occurred while retrieving orders with filtering ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException("Failed to retrieve orders with filtering", ex);
            }
        }

        /// <summary>
        /// Saves all pending changes to the database
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of affected rows</returns>
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while saving changes to database");
                throw;
            }
        }
    }
}
