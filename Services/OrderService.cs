using Microsoft.EntityFrameworkCore;
using OrderProcessingSystem.Data;
using OrderProcessingSystem.Models;
using OrderProcessingSystem.Repositories;
using System.Text.Json;

namespace OrderProcessingSystem.Services
{
    /// <summary>
    /// Interface for order business logic operations
    /// </summary>
    public interface IOrderService
    {
        Task<OrderResponse> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken = default);
        Task<OrderResponse?> GetOrderByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<OrderResponse?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
        Task<IEnumerable<OrderResponse>> GetAllOrdersAsync(CancellationToken cancellationToken = default);
        Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status, CancellationToken cancellationToken = default);
        Task<bool> DeleteOrderAsync(int id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Service implementation for order business logic with idempotency and error handling
    /// </summary>
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<OrderService> _logger;

        public OrderService(
            IOrderRepository orderRepository,
            AppDbContext dbContext,
            ILogger<OrderService> logger)
        {
            _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new order with idempotency support and comprehensive validation
        /// </summary>
        /// <param name="request">Order creation request containing all order details</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>OrderResponse containing the created order details</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when order creation fails</exception>
        public async Task<OrderResponse> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                _logger.LogError("CreateOrderAsync called with null request");
                throw new ArgumentNullException(nameof(request), "Order request cannot be null");
            }

            _logger.LogInformation(
                "Processing order creation request for Customer: {CustomerId}, Product: {ProductName}, IdempotencyKey: {IdempotencyKey}",
                request.CustomerId, request.ProductName, request.IdempotencyKey);

            // Validate request
            ValidateOrderRequest(request);

            // Use a database transaction for idempotency check and order creation
            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Check for idempotency - if this key was already processed, return the existing order
                var existingIdempotencyKey = await _dbContext.Set<IdempotencyKey>()
                    .FirstOrDefaultAsync(ik => ik.Key == request.IdempotencyKey, cancellationToken);

                if (existingIdempotencyKey != null)
                {
                    _logger.LogWarning(
                        "Duplicate request detected with IdempotencyKey: {IdempotencyKey}, returning existing order",
                        request.IdempotencyKey);

                    // Retrieve the existing order
                    var existingOrder = await _orderRepository.GetOrderByIdAsync(
                        existingIdempotencyKey.OrderId, 
                        cancellationToken);

                    if (existingOrder != null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return OrderResponse.FromOrder(existingOrder);
                    }
                }

                // Generate unique order number
                var orderNumber = await GenerateOrderNumberAsync(cancellationToken);

                // Create order entity
                var order = new Order
                {
                    OrderNumber = orderNumber,
                    CustomerId = request.CustomerId,
                    ProductName = request.ProductName,
                    Quantity = request.Quantity,
                    Price = request.Price,
                    IdempotencyKey = request.IdempotencyKey,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    RetryCount = 0,
                    IsDeleted = false
                };

                // Calculate total amount
                order.CalculateTotalAmount();

                // Additional business validation
                ValidateOrderEntity(order);

                // Save order to database
                var createdOrder = await _orderRepository.CreateOrderAsync(order, cancellationToken);

                // Store idempotency key with expiration (24 hours)
                var idempotencyKey = new IdempotencyKey
                {
                    Key = request.IdempotencyKey,
                    OrderId = createdOrder.Id,
                    Status = "Processed",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(24),
                    ResponseData = JsonSerializer.Serialize(OrderResponse.FromOrder(createdOrder))
                };

                await _dbContext.Set<IdempotencyKey>().AddAsync(idempotencyKey, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);

                // Commit transaction
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully created order with ID: {OrderId}, OrderNumber: {OrderNumber}, TotalAmount: {TotalAmount}",
                    createdOrder.Id, createdOrder.OrderNumber, createdOrder.TotalAmount);

                return OrderResponse.FromOrder(createdOrder);
            }
            catch (DbUpdateException dbEx)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(dbEx, 
                    "Database error during order creation for Customer: {CustomerId}, IdempotencyKey: {IdempotencyKey}",
                    request.CustomerId, request.IdempotencyKey);
                
                throw new InvalidOperationException(
                    "Failed to create order due to database error. Please try again.", dbEx);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, 
                    "Unexpected error during order creation for Customer: {CustomerId}, IdempotencyKey: {IdempotencyKey}",
                    request.CustomerId, request.IdempotencyKey);
                
                throw new InvalidOperationException(
                    $"An error occurred while creating the order: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Retrieves an order by its ID
        /// </summary>
        public async Task<OrderResponse?> GetOrderByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            if (id <= 0)
            {
                _logger.LogWarning("GetOrderByIdAsync called with invalid ID: {OrderId}", id);
                throw new ArgumentException("Order ID must be greater than zero", nameof(id));
            }

            _logger.LogInformation("Retrieving order with ID: {OrderId}", id);

            var order = await _orderRepository.GetOrderByIdAsync(id, cancellationToken);
            
            if (order == null)
            {
                _logger.LogWarning("Order not found with ID: {OrderId}", id);
                return null;
            }

            return OrderResponse.FromOrder(order);
        }

        /// <summary>
        /// Retrieves an order by its order number
        /// </summary>
        public async Task<OrderResponse?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                _logger.LogWarning("GetOrderByOrderNumberAsync called with empty order number");
                throw new ArgumentException("Order number cannot be empty", nameof(orderNumber));
            }

            _logger.LogInformation("Retrieving order with OrderNumber: {OrderNumber}", orderNumber);

            var order = await _orderRepository.GetOrderByOrderNumberAsync(orderNumber, cancellationToken);
            
            if (order == null)
            {
                _logger.LogWarning("Order not found with OrderNumber: {OrderNumber}", orderNumber);
                return null;
            }

            return OrderResponse.FromOrder(order);
        }

        /// <summary>
        /// Retrieves all orders
        /// </summary>
        public async Task<IEnumerable<OrderResponse>> GetAllOrdersAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving all orders");

            var orders = await _orderRepository.GetAllOrdersAsync(cancellationToken);
            
            return orders.Select(OrderResponse.FromOrder).ToList();
        }

        /// <summary>
        /// Updates the status of an existing order
        /// </summary>
        public async Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status, CancellationToken cancellationToken = default)
        {
            if (orderId <= 0)
            {
                throw new ArgumentException("Order ID must be greater than zero", nameof(orderId));
            }

            _logger.LogInformation("Updating order status for OrderId: {OrderId} to {Status}", orderId, status);

            var order = await _orderRepository.GetOrderByIdAsync(orderId, cancellationToken);
            
            if (order == null)
            {
                _logger.LogWarning("Cannot update status - Order not found with ID: {OrderId}", orderId);
                return false;
            }

            order.Status = status;
            order.UpdatedAt = DateTime.UtcNow;

            if (status == OrderStatus.Completed || status == OrderStatus.Failed)
            {
                order.ProcessedAt = DateTime.UtcNow;
            }

            var result = await _orderRepository.UpdateOrderAsync(order, cancellationToken);

            if (result)
            {
                _logger.LogInformation("Successfully updated order status for OrderId: {OrderId} to {Status}", 
                    orderId, status);
            }
            else
            {
                _logger.LogWarning("Failed to update order status for OrderId: {OrderId}", orderId);
            }

            return result;
        }

        /// <summary>
        /// Soft deletes an order (sets IsDeleted flag)
        /// </summary>
        public async Task<bool> DeleteOrderAsync(int id, CancellationToken cancellationToken = default)
        {
            if (id <= 0)
            {
                throw new ArgumentException("Order ID must be greater than zero", nameof(id));
            }

            _logger.LogInformation("Deleting order with ID: {OrderId}", id);

            var result = await _orderRepository.DeleteOrderAsync(id, cancellationToken);

            if (result)
            {
                _logger.LogInformation("Successfully deleted order with ID: {OrderId}", id);
            }
            else
            {
                _logger.LogWarning("Failed to delete order with ID: {OrderId}", id);
            }

            return result;
        }

        #region Private Helper Methods

        /// <summary>
        /// Validates the order request before processing
        /// </summary>
        private void ValidateOrderRequest(OrderRequest request)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                errors.Add("Idempotency key is required");
            }

            if (string.IsNullOrWhiteSpace(request.CustomerId))
            {
                errors.Add("Customer ID is required");
            }

            if (string.IsNullOrWhiteSpace(request.ProductName))
            {
                errors.Add("Product name is required");
            }

            if (request.Quantity <= 0)
            {
                errors.Add("Quantity must be greater than zero");
            }

            if (request.Quantity > 10000)
            {
                errors.Add("Quantity cannot exceed 10,000 units");
            }

            if (request.Price <= 0)
            {
                errors.Add("Price must be greater than zero");
            }

            if (request.Price > 1000000)
            {
                errors.Add("Price cannot exceed 1,000,000");
            }

            if (errors.Any())
            {
                var errorMessage = string.Join("; ", errors);
                _logger.LogWarning("Order request validation failed: {Errors}", errorMessage);
                throw new ArgumentException($"Invalid order request: {errorMessage}");
            }
        }

        /// <summary>
        /// Validates the order entity for business rules
        /// </summary>
        private void ValidateOrderEntity(Order order)
        {
            // Check for reasonable total amount (max 10 million)
            if (order.TotalAmount > 10_000_000)
            {
                _logger.LogWarning(
                    "Order total amount exceeds maximum allowed: {TotalAmount} for Customer: {CustomerId}",
                    order.TotalAmount, order.CustomerId);
                throw new InvalidOperationException(
                    $"Order total amount ({order.TotalAmount:C}) exceeds maximum allowed (10,000,000)");
            }

            // Check for minimum order amount (e.g., $1)
            if (order.TotalAmount < 1)
            {
                _logger.LogWarning(
                    "Order total amount below minimum: {TotalAmount} for Customer: {CustomerId}",
                    order.TotalAmount, order.CustomerId);
                throw new InvalidOperationException(
                    "Order total amount must be at least $1.00");
            }
        }

        /// <summary>
        /// Generates a unique order number with format: ORD-YYYYMMDD-XXXXXX
        /// </summary>
        private async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken)
        {
            const int maxAttempts = 5;
            var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Generate random 6-digit suffix
                var random = new Random();
                var suffix = random.Next(100000, 999999);
                var orderNumber = $"ORD-{datePrefix}-{suffix}";

                // Check if this order number already exists
                var existingOrder = await _orderRepository.GetOrderByOrderNumberAsync(orderNumber, cancellationToken);
                
                if (existingOrder == null)
                {
                    _logger.LogDebug("Generated unique order number: {OrderNumber}", orderNumber);
                    return orderNumber;
                }

                _logger.LogDebug("Order number collision detected: {OrderNumber}, retrying...", orderNumber);
            }

            // If we couldn't generate a unique number after max attempts, use a GUID suffix
            var guidSuffix = Guid.NewGuid().ToString("N")[..6].ToUpper();
            var fallbackOrderNumber = $"ORD-{datePrefix}-{guidSuffix}";
            
            _logger.LogWarning("Using GUID-based order number after {MaxAttempts} attempts: {OrderNumber}", 
                maxAttempts, fallbackOrderNumber);
            
            return fallbackOrderNumber;
        }

        #endregion
    }
}
