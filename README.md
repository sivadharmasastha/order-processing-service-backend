# Order Processing Service - Backend

A production-grade order processing service built with .NET Core, featuring comprehensive order management, idempotency support, advanced filtering, and pagination.

## 🚀 Features

### Core Functionality
- **Order Creation** with idempotency support to prevent duplicate orders
- **Order Retrieval** with multiple access patterns (by ID, order number, or advanced filtering)
- **Order Status Management** with workflow state transitions
- **Soft Delete** functionality for order archiving
- **Comprehensive Logging** at all levels
- **Error Handling** with proper exception management and retry logic

### Production-Level GetAllOrders Implementation

The service now includes **production-ready order retrieval** with two methods:

#### 1. Simple GetAllOrders (Legacy/Internal Use)
```http
GET /api/orders/all
```
- Returns all orders without pagination
- **⚠️ WARNING:** Not recommended for production use with large datasets
- Use for: Internal tools, small datasets, or administrative purposes

#### 2. Advanced GetOrders with Filtering & Pagination (Recommended)
```http
GET /api/orders?pageNumber=1&pageSize=20&status=Pending&sortBy=CreatedAt&sortOrder=desc
```

**Query Parameters:**
- `pageNumber` (default: 1) - Page number for pagination
- `pageSize` (default: 20, max: 100) - Items per page
- `status` - Filter by order status (Pending, Processing, Completed, Failed, Cancelled)
- `customerId` - Filter by specific customer
- `productName` - Search by product name (partial match)
- `fromDate` - Filter orders created from this date (UTC)
- `toDate` - Filter orders created until this date (UTC)
- `minAmount` - Minimum order total amount
- `maxAmount` - Maximum order total amount
- `searchTerm` - Universal search across order number, customer ID, and product name
- `sortBy` - Sort field (OrderNumber, CreatedAt, TotalAmount, Status, CustomerId, ProductName)
- `sortOrder` - Sort direction (asc or desc)
- `includeDeleted` (default: false) - Include soft-deleted orders

**Response Structure:**
```json
{
  "success": true,
  "data": {
    "data": [
      {
        "id": 1,
        "orderNumber": "ORD-20260502-123456",
        "customerId": "CUST-001",
        "productName": "Premium Widget",
        "quantity": 5,
        "price": 99.99,
        "totalAmount": 499.95,
        "status": "Pending",
        "createdAt": "2026-05-02T10:30:00Z",
        "updatedAt": null,
        "processedAt": null,
        "errorMessage": null,
        "retryCount": 0
      }
    ],
    "totalCount": 150,
    "pageNumber": 1,
    "pageSize": 20,
    "totalPages": 8,
    "hasPreviousPage": false,
    "hasNextPage": true
  },
  "message": "Retrieved 20 orders (Page 1 of 8)",
  "timestamp": "2026-05-02T10:35:00Z"
}
```

### Example Use Cases

#### Get all pending orders
```http
GET /api/orders?status=Pending&pageSize=50
```

#### Search for specific customer's orders
```http
GET /api/orders?customerId=CUST-001&sortBy=CreatedAt&sortOrder=desc
```

#### Find orders within a date range
```http
GET /api/orders?fromDate=2026-05-01&toDate=2026-05-02
```

#### Search across multiple fields
```http
GET /api/orders?searchTerm=Widget&pageSize=25
```

#### Filter by amount range
```http
GET /api/orders?minAmount=100&maxAmount=1000&sortBy=TotalAmount&sortOrder=desc
```

## 🏗️ Architecture

### Layered Architecture
```
Controllers/         → API endpoints and request handling
  ├── OrdersController.cs
Services/            → Business logic layer
  ├── OrderService.cs
  ├── IdempotencyService.cs
  └── RetryService.cs
Repositories/        → Data access layer
  └── OrderRepository.cs
Models/              → Domain models and DTOs
  ├── Order.cs
  ├── OrderRequest.cs
  ├── OrderResponse.cs
  └── OrderQueryParameters.cs
Data/                → Database context and migrations
  └── AppDbContext.cs
```

### Key Components

#### OrderService
- **GetAllOrdersAsync()** - Simple retrieval of all orders
- **GetOrdersAsync(OrderQueryParameters)** - Production-level paginated and filtered retrieval
- **CreateOrderAsync()** - Create orders with idempotency
- **GetOrderByIdAsync()** - Retrieve single order by ID
- **GetOrderByOrderNumberAsync()** - Retrieve single order by order number
- **UpdateOrderStatusAsync()** - Update order status
- **DeleteOrderAsync()** - Soft delete orders

#### OrderRepository
- **GetAllOrdersAsync()** - Basic retrieval from database
- **GetOrdersWithFilteringAsync()** - Advanced filtering with IQueryable
- Efficient database queries with AsNoTracking() for read operations
- Comprehensive error handling and logging

## 🔒 Production Features

### Performance Optimizations
- **Pagination** - Prevents loading large datasets into memory
- **AsNoTracking()** - Improved read performance for queries
- **Efficient Filtering** - Database-level filtering using EF.Functions.Like
- **Index-friendly Queries** - Optimized for database performance

### Error Handling
- Comprehensive try-catch blocks at all layers
- Detailed logging with correlation
- User-friendly error messages
- Proper HTTP status codes

### Validation
- Input parameter validation
- Business rule enforcement
- Date range validation
- Amount range validation

### Logging
- Structured logging with Serilog/Microsoft.Extensions.Logging
- Request/response logging
- Performance metrics
- Error tracking

## 📋 Prerequisites

- .NET 6.0 or higher
- SQL Server 2019+
- Redis (for caching)

## 🚦 Getting Started

1. **Setup Database**
   ```powershell
   .\Setup-Database.ps1
   ```

2. **Apply Migrations**
   ```powershell
   cd Data\Migrations
   .\Apply-Migration.ps1
   ```

3. **Run Application**
   ```powershell
   dotnet run
   ```

4. **Access API**
   - Swagger UI: `https://localhost:5001/swagger`
   - API Base URL: `https://localhost:5001/api`

## 📊 API Endpoints

### Orders
- `POST /api/orders` - Create new order
- `GET /api/orders/{id}` - Get order by ID
- `GET /api/orders/by-number/{orderNumber}` - Get order by order number
- `GET /api/orders/all` - Get all orders (unpaginated)
- `GET /api/orders` - Get orders with filtering and pagination ⭐
- `PATCH /api/orders/{id}/status` - Update order status
- `DELETE /api/orders/{id}` - Soft delete order

## 🧪 Testing

### Sample Requests

**Create Order:**
```bash
curl -X POST https://localhost:5001/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "idempotencyKey": "unique-key-123",
    "customerId": "CUST-001",
    "productName": "Premium Widget",
    "quantity": 5,
    "price": 99.99
  }'
```

**Get Paginated Orders:**
```bash
curl -X GET "https://localhost:5001/api/orders?pageNumber=1&pageSize=20&status=Pending" \
  -H "Accept: application/json"
```

**Search Orders:**
```bash
curl -X GET "https://localhost:5001/api/orders?searchTerm=Widget&sortBy=TotalAmount&sortOrder=desc" \
  -H "Accept: application/json"
```

## 🔐 Best Practices Implemented

1. **Separation of Concerns** - Clear layer separation (Controller → Service → Repository)
2. **Dependency Injection** - All dependencies injected via constructor
3. **Async/Await** - All I/O operations are asynchronous
4. **CancellationToken** - Proper cancellation support for long-running operations
5. **Structured Logging** - Comprehensive logging at all levels
6. **Error Handling** - Graceful error handling with proper status codes
7. **Validation** - Input validation at multiple levels
8. **Idempotency** - Duplicate request prevention
9. **Soft Deletes** - Data preservation with IsDeleted flag
10. **Pagination** - Efficient handling of large datasets

## 📝 Notes

- The simple `GetAllOrdersAsync` is maintained for backward compatibility but should be avoided in production for large datasets
- Always use the paginated `GetOrdersAsync` endpoint for production workloads
- Maximum page size is capped at 100 items to prevent performance issues
- All dates are stored and returned in UTC
- Soft-deleted orders are excluded by default but can be included with `includeDeleted=true`

## 🤝 Contributing

This is a production-level implementation following industry best practices. When adding new features:
1. Follow the established layered architecture
2. Add comprehensive logging
3. Include error handling
4. Write XML documentation comments
5. Add validation at all entry points
6. Consider performance implications

## 📄 License

[Your License Here]

---

**Version:** 2.0  
**Last Updated:** May 2, 2026  
**Status:** Production Ready ✅
