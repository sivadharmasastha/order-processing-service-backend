using Microsoft.AspNetCore.Mvc;
using OrderProcessingSystem.Models;
using OrderProcessingSystem.Services;

namespace OrderProcessingSystem.Controllers
{
    /// <summary>
    /// API Controller for managing orders with comprehensive CRUD operations
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
        {
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new order with idempotency support
        /// </summary>
        /// <param name="request">Order creation request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Created order details</returns>
        /// <response code="201">Order created successfully</response>
        /// <response code="400">Invalid request data</response>
        /// <response code="409">Duplicate order (idempotency key already exists)</response>
        /// <response code="500">Internal server error</response>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<OrderResponse>>> CreateOrder(
            [FromBody] OrderRequest request, 
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Received order creation request for Customer: {CustomerId}", request?.CustomerId);

                var order = await _orderService.CreateOrderAsync(request!, cancellationToken);

                var response = ApiResponse<OrderResponse>.SuccessResponse(
                    order, 
                    "Order created successfully");

                return CreatedAtAction(
                    nameof(GetOrderById), 
                    new { id = order.Id }, 
                    response);
            }
            catch (ArgumentException argEx)
            {
                _logger.LogWarning(argEx, "Invalid order request");
                return BadRequest(ApiResponse<OrderResponse>.ErrorResponse(
                    "Invalid order data", 
                    new List<string> { argEx.Message }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order");
                return StatusCode(500, ApiResponse<OrderResponse>.ErrorResponse(
                    "An error occurred while creating the order"));
            }
        }

        /// <summary>
        /// Retrieves a specific order by ID
        /// </summary>
        /// <param name="id">Order ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Order details</returns>
        /// <response code="200">Order found and returned</response>
        /// <response code="404">Order not found</response>
        /// <response code="500">Internal server error</response>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<OrderResponse>>> GetOrderById(
            int id, 
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Retrieving order with ID: {OrderId}", id);

                var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);

                if (order == null)
                {
                    return NotFound(ApiResponse<OrderResponse>.ErrorResponse(
                        $"Order with ID {id} not found"));
                }

                return Ok(ApiResponse<OrderResponse>.SuccessResponse(
                    order, 
                    "Order retrieved successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order with ID: {OrderId}", id);
                return StatusCode(500, ApiResponse<OrderResponse>.ErrorResponse(
                    "An error occurred while retrieving the order"));
            }
        }

        /// <summary>
        /// Retrieves a specific order by order number
        /// </summary>
        /// <param name="orderNumber">Order number</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Order details</returns>
        /// <response code="200">Order found and returned</response>
        /// <response code="404">Order not found</response>
        /// <response code="500">Internal server error</response>
        [HttpGet("by-number/{orderNumber}")]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<OrderResponse>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<OrderResponse>>> GetOrderByNumber(
            string orderNumber, 
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Retrieving order with OrderNumber: {OrderNumber}", orderNumber);

                var order = await _orderService.GetOrderByOrderNumberAsync(orderNumber, cancellationToken);

                if (order == null)
                {
                    return NotFound(ApiResponse<OrderResponse>.ErrorResponse(
                        $"Order with number {orderNumber} not found"));
                }

                return Ok(ApiResponse<OrderResponse>.SuccessResponse(
                    order, 
                    "Order retrieved successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving order with OrderNumber: {OrderNumber}", orderNumber);
                return StatusCode(500, ApiResponse<OrderResponse>.ErrorResponse(
                    "An error occurred while retrieving the order"));
            }
        }

        /// <summary>
        /// Retrieves all orders (simple version - WARNING: Not recommended for large datasets)
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of all orders</returns>
        /// <response code="200">Orders retrieved successfully</response>
        /// <response code="500">Internal server error</response>
        /// <remarks>
        /// WARNING: This endpoint retrieves ALL orders without pagination.
        /// For production use with large datasets, use the GET /api/orders endpoint instead.
        /// </remarks>
        [HttpGet("all")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<OrderResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<OrderResponse>>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<IEnumerable<OrderResponse>>>> GetAllOrders(
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogWarning("GetAllOrders endpoint called - Consider using paginated endpoint for better performance");

                var orders = await _orderService.GetAllOrdersAsync(cancellationToken);

                return Ok(ApiResponse<IEnumerable<OrderResponse>>.SuccessResponse(
                    orders, 
                    $"Retrieved {orders.Count()} orders"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all orders");
                return StatusCode(500, ApiResponse<IEnumerable<OrderResponse>>.ErrorResponse(
                    "An error occurred while retrieving orders"));
            }
        }

        /// <summary>
        /// Retrieves orders with advanced filtering, sorting, and pagination (Production-recommended)
        /// </summary>
        /// <param name="parameters">Query parameters for filtering and pagination</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Paginated list of orders</returns>
        /// <response code="200">Orders retrieved successfully</response>
        /// <response code="400">Invalid query parameters</response>
        /// <response code="500">Internal server error</response>
        /// <remarks>
        /// Sample request:
        ///     GET /api/orders?pageNumber=1&amp;pageSize=20&amp;status=Pending&amp;sortBy=CreatedAt&amp;sortOrder=desc
        ///     
        /// Query parameters:
        /// - pageNumber: Page number (default: 1)
        /// - pageSize: Items per page (default: 20, max: 100)
        /// - status: Filter by status (Pending, Processing, Completed, Failed, Cancelled)
        /// - customerId: Filter by customer ID
        /// - productName: Filter by product name (partial match)
        /// - fromDate: Filter orders from this date (UTC)
        /// - toDate: Filter orders until this date (UTC)
        /// - minAmount: Minimum order amount
        /// - maxAmount: Maximum order amount
        /// - searchTerm: Search across order number, customer ID, and product name
        /// - sortBy: Sort field (OrderNumber, CreatedAt, TotalAmount, Status, CustomerId)
        /// - sortOrder: Sort direction (asc or desc)
        /// - includeDeleted: Include soft-deleted orders (default: false)
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PaginatedResponse<OrderResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<PaginatedResponse<OrderResponse>>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<PaginatedResponse<OrderResponse>>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<OrderResponse>>>> GetOrders(
            [FromQuery] OrderQueryParameters parameters, 
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation(
                    "Retrieving orders with pagination - Page: {PageNumber}, PageSize: {PageSize}",
                    parameters.PageNumber, parameters.PageSize);

                var paginatedOrders = await _orderService.GetOrdersAsync(parameters, cancellationToken);

                return Ok(ApiResponse<PaginatedResponse<OrderResponse>>.SuccessResponse(
                    paginatedOrders,
                    $"Retrieved {paginatedOrders.Data.Count} orders (Page {paginatedOrders.PageNumber} of {paginatedOrders.TotalPages})"));
            }
            catch (ArgumentException argEx)
            {
                _logger.LogWarning(argEx, "Invalid query parameters");
                return BadRequest(ApiResponse<PaginatedResponse<OrderResponse>>.ErrorResponse(
                    "Invalid query parameters",
                    new List<string> { argEx.Message }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving orders with pagination");
                return StatusCode(500, ApiResponse<PaginatedResponse<OrderResponse>>.ErrorResponse(
                    "An error occurred while retrieving orders"));
            }
        }

        /// <summary>
        /// Updates the status of an existing order
        /// </summary>
        /// <param name="id">Order ID</param>
        /// <param name="request">Status update request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Success status</returns>
        /// <response code="200">Order status updated successfully</response>
        /// <response code="400">Invalid request data</response>
        /// <response code="404">Order not found</response>
        /// <response code="500">Internal server error</response>
        [HttpPatch("{id:int}/status")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<bool>>> UpdateOrderStatus(
            int id,
            [FromBody] UpdateOrderStatusRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Updating status for order ID: {OrderId} to {Status}", id, request.Status);

                if (!Enum.TryParse<OrderStatus>(request.Status, true, out var status))
                {
                    return BadRequest(ApiResponse<bool>.ErrorResponse(
                        $"Invalid status value: {request.Status}"));
                }

                var result = await _orderService.UpdateOrderStatusAsync(id, status, cancellationToken);

                if (!result)
                {
                    return NotFound(ApiResponse<bool>.ErrorResponse(
                        $"Order with ID {id} not found"));
                }

                return Ok(ApiResponse<bool>.SuccessResponse(
                    true,
                    "Order status updated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating order status for ID: {OrderId}", id);
                return StatusCode(500, ApiResponse<bool>.ErrorResponse(
                    "An error occurred while updating the order status"));
            }
        }

        /// <summary>
        /// Soft deletes an order
        /// </summary>
        /// <param name="id">Order ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Success status</returns>
        /// <response code="200">Order deleted successfully</response>
        /// <response code="404">Order not found</response>
        /// <response code="500">Internal server error</response>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteOrder(
            int id, 
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Deleting order with ID: {OrderId}", id);

                var result = await _orderService.DeleteOrderAsync(id, cancellationToken);

                if (!result)
                {
                    return NotFound(ApiResponse<bool>.ErrorResponse(
                        $"Order with ID {id} not found"));
                }

                return Ok(ApiResponse<bool>.SuccessResponse(
                    true,
                    "Order deleted successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting order with ID: {OrderId}", id);
                return StatusCode(500, ApiResponse<bool>.ErrorResponse(
                    "An error occurred while deleting the order"));
            }
        }
    }

    /// <summary>
    /// Request model for updating order status
    /// </summary>
    public class UpdateOrderStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
