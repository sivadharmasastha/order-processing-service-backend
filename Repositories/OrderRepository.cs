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
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of all orders</returns>
        public async Task<IEnumerable<Order>> GetAllOrdersAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Retrieving all orders from database");

                var orders = await _context.Orders
                    .AsNoTracking() // Use AsNoTracking for read-only operations to improve performance
                    .OrderByDescending(o => o.CreatedAt) // Most recent orders first
                    .ToListAsync(cancellationToken);

                _logger.LogInformation("Successfully retrieved {OrderCount} orders", orders.Count);

                return orders;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while retrieving all orders");
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
            try
            {
                _logger.LogInformation("Retrieving order with ID: {OrderId}", id);

                var order = await _context.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

                if (order == null)
                {
                    _logger.LogWarning("Order with ID: {OrderId} not found", id);
                }
                else
                {
                    _logger.LogInformation("Successfully retrieved order with ID: {OrderId}", id);
                }

                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while retrieving order with ID: {OrderId}", id);
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

            try
            {
                _logger.LogInformation("Retrieving order with OrderNumber: {OrderNumber}", orderNumber);

                var order = await _context.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);

                if (order == null)
                {
                    _logger.LogWarning("Order with OrderNumber: {OrderNumber} not found", orderNumber);
                }
                else
                {
                    _logger.LogInformation("Successfully retrieved order with OrderNumber: {OrderNumber}", orderNumber);
                }

                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while retrieving order with OrderNumber: {OrderNumber}", orderNumber);
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

            try
            {
                _logger.LogInformation("Updating order with ID: {OrderId}", order.Id);

                // Update timestamp
                order.UpdatedAt = DateTime.UtcNow;

                // Recalculate total amount if quantity or price changed
                order.CalculateTotalAmount();

                _context.Orders.Update(order);
                var rowsAffected = await _context.SaveChangesAsync(cancellationToken);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Successfully updated order with ID: {OrderId}", order.Id);
                    return true;
                }

                _logger.LogWarning("No rows were affected when updating order with ID: {OrderId}", order.Id);
                return false;
            }
            catch (DbUpdateConcurrencyException concurrencyEx)
            {
                _logger.LogError(concurrencyEx, "Concurrency conflict while updating order with ID: {OrderId}", order.Id);
                throw new InvalidOperationException($"Concurrency conflict occurred while updating order with ID: {order.Id}", concurrencyEx);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error while updating order with ID: {OrderId}", order.Id);
                throw new InvalidOperationException($"Failed to update order: {dbEx.Message}", dbEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while updating order with ID: {OrderId}", order.Id);
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
            try
            {
                _logger.LogInformation("Attempting to delete order with ID: {OrderId}", id);

                var order = await _context.Orders.FindAsync(new object[] { id }, cancellationToken);

                if (order == null)
                {
                    _logger.LogWarning("Order with ID: {OrderId} not found for deletion", id);
                    return false;
                }

                // Soft delete
                order.IsDeleted = true;
                order.UpdatedAt = DateTime.UtcNow;

                _context.Orders.Update(order);
                var rowsAffected = await _context.SaveChangesAsync(cancellationToken);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Successfully deleted order with ID: {OrderId}", id);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while deleting order with ID: {OrderId}", id);
                throw new InvalidOperationException($"Failed to delete order with ID: {id}", ex);
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
