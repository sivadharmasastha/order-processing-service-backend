using System.Net;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Polly.Timeout;
using Polly.CircuitBreaker;

namespace OrderProcessingSystem.Middleware
{
    /// <summary>
    /// Production-grade global exception handling middleware with:
    /// - Correlation ID tracking for distributed tracing
    /// - Comprehensive error classification (transient vs permanent)
    /// - Structured logging with contextual information
    /// - Performance metrics and monitoring
    /// - Retry recommendations for clients
    /// - Security-aware error responses (no sensitive data leakage)
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        // Error categories for monitoring and alerting
        private enum ErrorCategory
        {
            ClientError,        // 4xx - Client's fault
            ServerError,        // 5xx - Server's fault
            TransientError,     // Temporary, retry may succeed
            PermanentError      // Permanent, retry will not help
        }

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = GetOrCreateCorrelationId(context);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await HandleExceptionAsync(context, ex, correlationId, stopwatch.ElapsedMilliseconds);
            }
        }

        private async Task HandleExceptionAsync(
            HttpContext context, 
            Exception exception, 
            string correlationId, 
            long elapsedMs)
        {
            // Classify the error
            var (statusCode, category, isRetryable) = ClassifyException(exception);
            
            // Log with structured data for monitoring and diagnostics
            LogException(exception, context, correlationId, statusCode, category, elapsedMs);

            // Set response headers
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;
            context.Response.Headers.Add("X-Correlation-ID", correlationId);
            
            if (isRetryable)
            {
                // Add Retry-After header for transient errors (in seconds)
                context.Response.Headers.Add("Retry-After", GetRetryAfterSeconds(exception).ToString());
            }

            // Build error response (security-aware, no sensitive data)
            var response = new ErrorResponse
            {
                StatusCode = statusCode,
                Message = GetUserFriendlyMessage(exception, category),
                ErrorCode = GetErrorCode(exception),
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow,
                IsRetryable = isRetryable,
                
                // Include detailed information only in development
                DetailedMessage = _environment.IsDevelopment() ? exception.Message : null,
                StackTrace = _environment.IsDevelopment() ? exception.StackTrace : null,
                InnerException = _environment.IsDevelopment() && exception.InnerException != null 
                    ? exception.InnerException.Message 
                    : null
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = _environment.IsDevelopment()
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
        }

        /// <summary>
        /// Classifies exception into status code, category, and retry recommendation
        /// </summary>
        private (int statusCode, ErrorCategory category, bool isRetryable) ClassifyException(Exception exception)
        {
            // Handle InvalidOperationException separately due to multiple conditions
            if (exception is InvalidOperationException invalidOpEx)
            {
                if (invalidOpEx.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                    invalidOpEx.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    return ((int)HttpStatusCode.Conflict, ErrorCategory.ClientError, false);
                }
                return ((int)HttpStatusCode.BadRequest, ErrorCategory.ClientError, false);
            }

            // Handle cancellation exceptions (TaskCanceledException derives from OperationCanceledException)
            if (exception is OperationCanceledException)
            {
                if (exception is TaskCanceledException)
                {
                    return ((int)HttpStatusCode.RequestTimeout, ErrorCategory.TransientError, true);
                }
                return ((int)HttpStatusCode.RequestTimeout, ErrorCategory.TransientError, false);
            }

            return exception switch
            {
                // Client errors (4xx) - Not retryable
                ArgumentNullException => 
                    ((int)HttpStatusCode.BadRequest, ErrorCategory.ClientError, false),
                ArgumentException => 
                    ((int)HttpStatusCode.BadRequest, ErrorCategory.ClientError, false),
                KeyNotFoundException => 
                    ((int)HttpStatusCode.NotFound, ErrorCategory.ClientError, false),
                UnauthorizedAccessException => 
                    ((int)HttpStatusCode.Unauthorized, ErrorCategory.ClientError, false),
                
                // Database errors - More specific types first (DbUpdateConcurrencyException inherits from DbUpdateException)
                DbUpdateConcurrencyException =>
                    ((int)HttpStatusCode.Conflict, ErrorCategory.TransientError, true),
                DbUpdateException when IsTransientDatabaseError(exception) =>
                    ((int)HttpStatusCode.ServiceUnavailable, ErrorCategory.TransientError, true),
                DbUpdateException =>
                    ((int)HttpStatusCode.InternalServerError, ErrorCategory.PermanentError, false),
                
                // Timeout errors - Transient, retryable
                TimeoutException => 
                    ((int)HttpStatusCode.GatewayTimeout, ErrorCategory.TransientError, true),
                TimeoutRejectedException =>
                    ((int)HttpStatusCode.GatewayTimeout, ErrorCategory.TransientError, true),
                
                // Circuit breaker - Transient
                BrokenCircuitException =>
                    ((int)HttpStatusCode.ServiceUnavailable, ErrorCategory.TransientError, true),
                
                // Generic server errors
                _ => ((int)HttpStatusCode.InternalServerError, ErrorCategory.ServerError, false)
            };
        }

        /// <summary>
        /// Determines if a database error is transient (network, deadlock, timeout)
        /// </summary>
        private bool IsTransientDatabaseError(Exception exception)
        {
            var message = exception.Message.ToLowerInvariant();
            return message.Contains("timeout") ||
                   message.Contains("deadlock") ||
                   message.Contains("connection") ||
                   message.Contains("network") ||
                   message.Contains("transport") ||
                   exception.InnerException is TimeoutException;
        }

        /// <summary>
        /// Generates application-specific error codes for client tracking
        /// </summary>
        private string GetErrorCode(Exception exception)
        {
            // Handle InvalidOperationException separately
            if (exception is InvalidOperationException invalidOpEx)
            {
                if (invalidOpEx.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    return "ERR_DUPLICATE";
                }
                return "ERR_INVALID_OPERATION";
            }

            // Handle cancellation exceptions
            if (exception is OperationCanceledException)
            {
                return exception is TaskCanceledException 
                    ? "ERR_REQUEST_CANCELLED" 
                    : "ERR_OPERATION_CANCELLED";
            }

            return exception switch
            {
                ArgumentNullException => "ERR_MISSING_PARAMETER",
                ArgumentException => "ERR_INVALID_INPUT",
                KeyNotFoundException => "ERR_NOT_FOUND",
                UnauthorizedAccessException => "ERR_UNAUTHORIZED",
                DbUpdateConcurrencyException => "ERR_CONCURRENCY_CONFLICT",
                DbUpdateException => "ERR_DATABASE_UPDATE",
                TimeoutException => "ERR_TIMEOUT",
                TimeoutRejectedException => "ERR_TIMEOUT",
                BrokenCircuitException => "ERR_SERVICE_UNAVAILABLE",
                _ => "ERR_INTERNAL_SERVER"
            };
        }

        /// <summary>
        /// Provides user-friendly error messages without exposing sensitive information
        /// </summary>
        private string GetUserFriendlyMessage(Exception exception, ErrorCategory category)
        {
            // For transient errors, always encourage retry
            if (category == ErrorCategory.TransientError)
            {
                return exception switch
                {
                    TimeoutException or TimeoutRejectedException =>
                        "The request timed out. Please try again in a few moments.",
                    BrokenCircuitException =>
                        "The service is temporarily unavailable. Please try again shortly.",
                    DbUpdateConcurrencyException =>
                        "The resource was modified by another request. Please refresh and try again.",
                    DbUpdateException =>
                        "A temporary database issue occurred. Please try again.",
                    _ => "A temporary issue occurred. Please try again in a few moments."
                };
            }

            // Handle InvalidOperationException separately
            if (exception is InvalidOperationException invalidOpEx)
            {
                if (invalidOpEx.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    return "This operation would create a duplicate entry.";
                }
                return invalidOpEx.Message;
            }

            // Handle cancellation exceptions (TaskCanceledException derives from OperationCanceledException)
            if (exception is OperationCanceledException)
            {
                return exception is TaskCanceledException 
                    ? "The request was cancelled." 
                    : "The operation was cancelled.";
            }

            return exception switch
            {
                ArgumentNullException => "A required parameter was not provided.",
                ArgumentException => "Invalid input provided. Please check your request and try again.",
                KeyNotFoundException => "The requested resource was not found.",
                UnauthorizedAccessException => "You do not have permission to access this resource.",
                DbUpdateException => "Failed to save changes to the database.",
                _ => "An unexpected error occurred. Please try again later or contact support."
            };
        }

        /// <summary>
        /// Determines recommended retry delay in seconds based on exception type
        /// </summary>
        private int GetRetryAfterSeconds(Exception exception)
        {
            return exception switch
            {
                TimeoutException or TimeoutRejectedException => 5,
                DbUpdateConcurrencyException => 1,
                BrokenCircuitException => 30,
                DbUpdateException => 3,
                _ => 5
            };
        }

        /// <summary>
        /// Structured logging with comprehensive context for monitoring and diagnostics
        /// </summary>
        private void LogException(
            Exception exception,
            HttpContext context,
            string correlationId,
            int statusCode,
            ErrorCategory category,
            long elapsedMs)
        {
            var logLevel = category switch
            {
                ErrorCategory.ClientError => LogLevel.Warning,
                ErrorCategory.TransientError => LogLevel.Warning,
                _ => LogLevel.Error
            };

            _logger.Log(
                logLevel,
                exception,
                "Request failed | " +
                "Method: {HttpMethod} | " +
                "Path: {RequestPath} | " +
                "StatusCode: {StatusCode} | " +
                "Category: {ErrorCategory} | " +
                "ErrorType: {ExceptionType} | " +
                "Message: {ErrorMessage} | " +
                "CorrelationId: {CorrelationId} | " +
                "UserAgent: {UserAgent} | " +
                "RemoteIP: {RemoteIp} | " +
                "ElapsedMs: {ElapsedMs}",
                context.Request.Method,
                context.Request.Path,
                statusCode,
                category.ToString(),
                exception.GetType().Name,
                exception.Message,
                correlationId,
                context.Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown",
                context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                elapsedMs);

            // Log critical errors with full stack trace
            if (category == ErrorCategory.ServerError || category == ErrorCategory.PermanentError)
            {
                _logger.LogCritical(
                    exception,
                    "Critical error details | CorrelationId: {CorrelationId} | StackTrace: {StackTrace}",
                    correlationId,
                    exception.StackTrace);
            }
        }

        /// <summary>
        /// Gets or creates a correlation ID for request tracking across distributed systems
        /// </summary>
        private string GetOrCreateCorrelationId(HttpContext context)
        {
            // Check if correlation ID exists in request headers
            if (context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId) &&
                !string.IsNullOrWhiteSpace(correlationId))
            {
                return correlationId.ToString();
            }

            // Generate new correlation ID
            var newCorrelationId = Guid.NewGuid().ToString("N");
            context.Items["CorrelationId"] = newCorrelationId;
            return newCorrelationId;
        }
    }

    /// <summary>
    /// Production-grade error response model with comprehensive information
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// HTTP status code
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// User-friendly error message (safe for display)
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Application-specific error code for client-side handling
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Correlation ID for distributed tracing and support
        /// </summary>
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// UTC timestamp when error occurred
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Indicates if the client should retry the request
        /// </summary>
        public bool IsRetryable { get; set; }

        /// <summary>
        /// Detailed error message (development only)
        /// </summary>
        public string? DetailedMessage { get; set; }

        /// <summary>
        /// Stack trace (development only)
        /// </summary>
        public string? StackTrace { get; set; }

        /// <summary>
        /// Inner exception message (development only)
        /// </summary>
        public string? InnerException { get; set; }
    }
}
