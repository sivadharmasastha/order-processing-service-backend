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
        Task<PaginatedResponse<OrderResponse>> GetOrdersAsync(OrderQueryParameters parameters, CancellationToken cancellationToken = default);
        Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status, CancellationToken cancellationToken = default);
        Task<bool> DeleteOrderAsync(int id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Service implementation for order business logic with idempotency and error handling
    /// Enhanced with production-grade retry mechanisms for resilience
    /// </summary>
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly AppDbContext _dbContext;
        private readonly IIdempotencyService _idempotencyService;
        private readonly RetryService _retryService;
        private readonly ILogger<OrderService> _logger;

        public OrderService(
            IOrderRepository orderRepository,
            AppDbContext dbContext,
            IIdempotencyService idempotencyService,
            RetryService retryService,
            ILogger<OrderService> logger)
        {
            _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _retryService = retryService ?? throw new ArgumentNullException(nameof(retryService));
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

            // Use idempotency service with distributed locking and caching
            var idempotencyResult = await _idempotencyService.CheckAndProcessAsync(
                request.IdempotencyKey,
                async () => await ProcessOrderCreationAsync(request, cancellationToken),
                cancellationToken);

            var orderResponse = (OrderResponse)idempotencyResult.Response!;

            if (idempotencyResult.WasCached)
            {
                _logger.LogInformation(
                    "Returned cached order for IdempotencyKey: {IdempotencyKey}, OrderId: {OrderId} ({ElapsedMs}ms)",
                    request.IdempotencyKey, orderResponse.Id, idempotencyResult.ExecutionTimeMs);
            }
            else
            {
                _logger.LogInformation(
                    "Created new order for IdempotencyKey: {IdempotencyKey}, OrderId: {OrderId} ({ElapsedMs}ms)",
                    request.IdempotencyKey, orderResponse.Id, idempotencyResult.ExecutionTimeMs);
            }

            return orderResponse;
        }

        /// <summary>
        /// Internal method to process order creation (called by idempotency service)
        /// Enhanced with retry logic for database operations
        /// </summary>
        private async Task<OrderResponse> ProcessOrderCreationAsync(OrderRequest request, CancellationToken cancellationToken)
        {
            // Use a database transaction for order creation
            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Generate unique order number (with built-in retry logic)
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

                // Save order to database with retry logic for transient failures
                var createdOrder = await _retryService.ExecuteDatabaseOperationAsync(
                    async () => await _orderRepository.CreateOrderAsync(order, cancellationToken),
                    retryCount: 5,
                    operationName: $"CreateOrder_{request.IdempotencyKey}",
                    cancellationToken: cancellationToken);

                // Create response
                var orderResponse = OrderResponse.FromOrder(createdOrder);

                // Store idempotency key with response (both Redis and Database)
                await _idempotencyService.StoreAsync(
                    request.IdempotencyKey,
                    createdOrder.Id,
                    orderResponse,
                    expiryHours: 24,
                    cancellationToken);

                // Commit transaction
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully created order with ID: {OrderId}, OrderNumber: {OrderNumber}, TotalAmount: {TotalAmount}",
                    createdOrder.Id, createdOrder.OrderNumber, createdOrder.TotalAmount);

                return orderResponse;
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
        /// Retrieves an order by its ID with retry logic for transient failures
        /// </summary>
        public async Task<OrderResponse?> GetOrderByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            if (id <= 0)
            {
                _logger.LogWarning("GetOrderByIdAsync called with invalid ID: {OrderId}", id);
                throw new ArgumentException("Order ID must be greater than zero", nameof(id));
            }

            _logger.LogInformation("Retrieving order with ID: {OrderId}", id);

            // Wrap database operation with retry logic
            var order = await _retryService.ExecuteDatabaseOperationAsync(
                async () => await _orderRepository.GetOrderByIdAsync(id, cancellationToken),
                retryCount: 5,
                operationName: $"GetOrderById_{id}",
                cancellationToken: cancellationToken);
            
            if (order == null)
            {
                _logger.LogWarning("Order not found with ID: {OrderId}", id);
                return null;
            }

            return OrderResponse.FromOrder(order);
        }

        /// <summary>
        /// Retrieves an order by its order number with retry logic for transient failures
        /// </summary>
        public async Task<OrderResponse?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                _logger.LogWarning("GetOrderByOrderNumberAsync called with empty order number");
                throw new ArgumentException("Order number cannot be empty", nameof(orderNumber));
            }

            _logger.LogInformation("Retrieving order with OrderNumber: {OrderNumber}", orderNumber);

            // Wrap database operation with retry logic
            var order = await _retryService.ExecuteDatabaseOperationAsync(
                async () => await _orderRepository.GetOrderByOrderNumberAsync(orderNumber, cancellationToken),
                retryCount: 5,
                operationName: $"GetOrderByOrderNumber_{orderNumber}",
                cancellationToken: cancellationToken);
            
            if (order == null)
            {
                _logger.LogWarning("Order not found with OrderNumber: {OrderNumber}", orderNumber);
                return null;
            }

            return OrderResponse.FromOrder(order);
        }

        /// <summary>
        /// Retrieves all orders with retry logic (simple version without pagination - use GetOrdersAsync for production workloads)
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Collection of all orders</returns>
        /// <remarks>
        /// WARNING: This method retrieves ALL orders without pagination. 
        /// For production use with large datasets, use GetOrdersAsync with pagination instead.
        /// Enhanced with retry logic for transient database failures.
        /// </remarks>
        public async Task<IEnumerable<OrderResponse>> GetAllOrdersAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving all orders (unpaginated)");

            try
            {
                // Wrap database operation with retry logic
                var orders = await _retryService.ExecuteDatabaseOperationAsync(
                    async () => await _orderRepository.GetAllOrdersAsync(cancellationToken),
                    retryCount: 5,
                    operationName: "GetAllOrders",
                    cancellationToken: cancellationToken);
                
                var orderResponses = orders.Select(OrderResponse.FromOrder).ToList();
                
                _logger.LogInformation("Successfully retrieved {OrderCount} orders", orderResponses.Count);
                
                return orderResponses;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while retrieving all orders");
                throw new InvalidOperationException("Failed to retrieve orders. Please try again later.", ex);
            }
        }

        /// <summary>
        /// Retrieves orders with advanced filtering, sorting, and pagination (Production-level method)
        /// Enhanced with retry logic for resilience against transient failures
        /// </summary>
        /// <param name="parameters">Query parameters for filtering, sorting, and pagination</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>Paginated response containing filtered and sorted orders</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters is null</exception>
        /// <exception cref="ArgumentException">Thrown when parameters validation fails</exception>
        public async Task<PaginatedResponse<OrderResponse>> GetOrdersAsync(
            OrderQueryParameters parameters, 
            CancellationToken cancellationToken = default)
        {
            if (parameters == null)
            {
                _logger.LogError("GetOrdersAsync called with null parameters");
                throw new ArgumentNullException(nameof(parameters), "Query parameters cannot be null");
            }

            try
            {
                // Validate parameters
                parameters.Validate();

                _logger.LogInformation(
                    "Retrieving orders with parameters - Page: {PageNumber}, PageSize: {PageSize}, Status: {Status}, " +
                    "CustomerId: {CustomerId}, SearchTerm: {SearchTerm}, SortBy: {SortBy}, SortOrder: {SortOrder}",
                    parameters.PageNumber, parameters.PageSize, parameters.Status ?? "All", 
                    parameters.CustomerId ?? "All", parameters.SearchTerm ?? "None", 
                    parameters.SortBy, parameters.SortOrder);

                // Retrieve filtered and paginated orders from repository with retry logic
                var (orders, totalCount) = await _retryService.ExecuteDatabaseOperationAsync(
                    async () => await _orderRepository.GetOrdersWithFilteringAsync(parameters, cancellationToken),
                    retryCount: 5,
                    operationName: $"GetOrdersFiltered_Page{parameters.PageNumber}",
                    cancellationToken: cancellationToken);

                // Convert to response DTOs
                var orderResponses = orders.Select(OrderResponse.FromOrder).ToList();

                // Build paginated response
                var paginatedResponse = new PaginatedResponse<OrderResponse>
                {
                    Data = orderResponses,
                    TotalCount = totalCount,
                    PageNumber = parameters.PageNumber,
                    PageSize = parameters.PageSize
                };

                _logger.LogInformation(
                    "Successfully retrieved {OrderCount} orders out of {TotalCount} total (Page {PageNumber}/{TotalPages})",
                    orderResponses.Count, totalCount, paginatedResponse.PageNumber, paginatedResponse.TotalPages);

                return paginatedResponse;
            }
            catch (ArgumentException argEx)
            {
                _logger.LogWarning(argEx, "Invalid query parameters provided");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while retrieving paginated orders");
                throw new InvalidOperationException(
                    "Failed to retrieve orders with the specified criteria. Please try again later.", ex);
            }
        }

        /// <summary>
        /// Updates the status of an existing order with retry logic for transient failures
        /// </summary>
        public async Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus status, CancellationToken cancellationToken = default)
        {
            if (orderId <= 0)
            {
                throw new ArgumentException("Order ID must be greater than zero", nameof(orderId));
            }

            _logger.LogInformation("Updating order status for OrderId: {OrderId} to {Status}", orderId, status);

            // Wrap database read operation with retry logic
            var order = await _retryService.ExecuteDatabaseOperationAsync(
                async () => await _orderRepository.GetOrderByIdAsync(orderId, cancellationToken),
                retryCount: 5,
                operationName: $"GetOrderForUpdate_{orderId}",
                cancellationToken: cancellationToken);
            
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

            // Wrap database update operation with retry logic
            var result = await _retryService.ExecuteDatabaseOperationAsync(
                async () => await _orderRepository.UpdateOrderAsync(order, cancellationToken),
                retryCount: 5,
                operationName: $"UpdateOrderStatus_{orderId}",
                cancellationToken: cancellationToken);

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
        /// Soft deletes an order (sets IsDeleted flag) with retry logic for transient failures
        /// </summary>
        public async Task<bool> DeleteOrderAsync(int id, CancellationToken cancellationToken = default)
        {
            if (id <= 0)
            {
                throw new ArgumentException("Order ID must be greater than zero", nameof(id));
            }

            _logger.LogInformation("Deleting order with ID: {OrderId}", id);

            // Wrap database delete operation with retry logic
            var result = await _retryService.ExecuteDatabaseOperationAsync(
                async () => await _orderRepository.DeleteOrderAsync(id, cancellationToken),
                retryCount: 5,
                operationName: $"DeleteOrder_{id}",
                cancellationToken: cancellationToken);

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
        /// Enhanced with retry logic for database collision checks
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

                // Check if this order number already exists with retry logic
                var existingOrder = await _retryService.ExecuteDatabaseOperationAsync(
                    async () => await _orderRepository.GetOrderByOrderNumberAsync(orderNumber, cancellationToken),
                    retryCount: 3,
                    operationName: $"CheckOrderNumberCollision_{orderNumber}",
                    cancellationToken: cancellationToken);
                
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
