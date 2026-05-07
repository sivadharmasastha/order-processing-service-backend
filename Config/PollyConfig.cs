using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Registry;
using Polly.Timeout;
using Polly.Wrap;
using System.Net;

namespace OrderProcessingSystem.Config
{
    /// <summary>
    /// Production-grade Polly resilience policies configuration with comprehensive error handling,
    /// circuit breakers, retries with jitter, timeouts, bulkhead isolation, and fallback mechanisms
    /// </summary>
    public static class PollyConfig
    {
        private static readonly Random _jitterer = new Random();
        private const string DatabaseRetryPolicy = "DatabaseRetry";
        private const string DatabaseCircuitBreakerPolicy = "DatabaseCircuitBreaker";
        private const string HttpRetryPolicy = "HttpRetry";
        private const string HttpCircuitBreakerPolicy = "HttpCircuitBreaker";
        private const string CombinedHttpPolicy = "CombinedHttp";

        /// <summary>
        /// Configures Polly policies for the application with policy registry
        /// </summary>
        /// <param name="services">Service collection</param>
        public static void ConfigurePolicies(IServiceCollection services)
        {
            // Add policy registry for centralized policy management
            var registry = services.AddPolicyRegistry();

            // Register database policies
            registry.Add(DatabaseRetryPolicy, GetDatabaseRetryPolicy());
            registry.Add(DatabaseCircuitBreakerPolicy, GetDatabaseCircuitBreakerPolicy());

            // Register HTTP policies
            registry.Add(HttpRetryPolicy, GetHttpRetryPolicy());
            registry.Add(HttpCircuitBreakerPolicy, GetHttpCircuitBreakerPolicy());
            registry.Add(CombinedHttpPolicy, GetCombinedHttpPolicy());
        }

        #region HTTP Policies

        /// <summary>
        /// Gets a retry policy for HTTP requests with exponential backoff and jitter
        /// Implements decorrelated jitter to prevent thundering herd problem
        /// </summary>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured retry policy</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetHttpRetryPolicy(int retryCount = 3, ILogger? logger = null)
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError() // 5xx and 408 errors
                .Or<TimeoutRejectedException>() // Timeout from Polly's TimeoutPolicy
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
                .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable)
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) // Exponential backoff
                                  + TimeSpan.FromMilliseconds(_jitterer.Next(0, 1000)), // Add jitter
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        var errorMessage = outcome.Exception?.Message ?? 
                                         $"HTTP {(int)outcome.Result.StatusCode} {outcome.Result.ReasonPhrase}";
                        
                        logger?.LogWarning(
                            outcome.Exception,
                            "HTTP request failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}. " +
                            "OperationKey: {OperationKey}",
                            retryAttempt, retryCount, timespan.TotalSeconds, errorMessage,
                            context.OperationKey ?? "Unknown");

                        // Store retry metrics in context
                        context["RetryCount"] = retryAttempt;
                        context["LastError"] = errorMessage;
                    });
        }

        /// <summary>
        /// Gets an advanced circuit breaker policy with half-open state handling
        /// Opens circuit after consecutive failures, gradually tests recovery
        /// </summary>
        /// <param name="failureThreshold">Failure rate (0.0 to 1.0) before opening circuit (default: 0.5)</param>
        /// <param name="samplingDuration">Duration to monitor failures (default: 10 seconds)</param>
        /// <param name="minimumThroughput">Minimum requests before calculating failure rate (default: 5)</param>
        /// <param name="durationOfBreak">Duration to keep circuit open (default: 30 seconds)</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured advanced circuit breaker policy</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetHttpCircuitBreakerPolicy(
            double failureThreshold = 0.5,
            int samplingDuration = 10,
            int minimumThroughput = 5,
            int durationOfBreak = 30,
            ILogger? logger = null)
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests ||
                                msg.StatusCode == HttpStatusCode.ServiceUnavailable)
                .AdvancedCircuitBreakerAsync(
                    failureThreshold: failureThreshold,
                    samplingDuration: TimeSpan.FromSeconds(samplingDuration),
                    minimumThroughput: minimumThroughput,
                    durationOfBreak: TimeSpan.FromSeconds(durationOfBreak),
                    onBreak: (result, breakDelay, context) =>
                    {
                        var errorMessage = result.Exception?.Message ?? 
                                         $"HTTP {(int)result.Result.StatusCode} {result.Result.ReasonPhrase}";
                        
                        logger?.LogError(
                            result.Exception,
                            "Circuit breaker OPENED for {BreakDuration}s. " +
                            "Failure threshold exceeded: {FailureThreshold}. " +
                            "Last error: {ErrorMessage}. OperationKey: {OperationKey}",
                            breakDelay.TotalSeconds, failureThreshold, errorMessage,
                            context.OperationKey ?? "Unknown");
                    },
                    onReset: (context) =>
                    {
                        logger?.LogInformation(
                            "Circuit breaker RESET. Service recovered. OperationKey: {OperationKey}",
                            context.OperationKey ?? "Unknown");
                    },
                    onHalfOpen: () =>
                    {
                        logger?.LogInformation(
                            "Circuit breaker HALF-OPEN. Testing if service recovered.");
                    });
        }

        /// <summary>
        /// Gets a timeout policy to prevent long-running HTTP requests
        /// </summary>
        /// <param name="timeoutSeconds">Timeout in seconds (default: 30)</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured timeout policy</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetHttpTimeoutPolicy(
            int timeoutSeconds = 30,
            ILogger? logger = null)
        {
            return Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(timeoutSeconds),
                TimeoutStrategy.Pessimistic, // Cancels the delegate
                onTimeoutAsync: (context, timespan, task) =>
                {
                    logger?.LogWarning(
                        "HTTP request timed out after {TimeoutSeconds}s. OperationKey: {OperationKey}",
                        timespan.TotalSeconds, context.OperationKey ?? "Unknown");
                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Gets a bulkhead isolation policy to limit concurrent HTTP requests
        /// Prevents resource exhaustion from too many parallel requests
        /// </summary>
        /// <param name="maxParallelization">Maximum parallel executions (default: 10)</param>
        /// <param name="maxQueuingActions">Maximum queued actions (default: 5)</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured bulkhead policy</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetHttpBulkheadPolicy(
            int maxParallelization = 10,
            int maxQueuingActions = 5,
            ILogger? logger = null)
        {
            return Policy.BulkheadAsync<HttpResponseMessage>(
                maxParallelization,
                maxQueuingActions,
                onBulkheadRejectedAsync: context =>
                {
                    logger?.LogWarning(
                        "Bulkhead rejection. Too many concurrent requests. " +
                        "MaxParallel: {MaxParallel}, MaxQueue: {MaxQueue}, OperationKey: {OperationKey}",
                        maxParallelization, maxQueuingActions, context.OperationKey ?? "Unknown");
                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Gets a fallback policy with a default response for HTTP failures
        /// </summary>
        /// <param name="fallbackResponse">Fallback response to return</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured fallback policy</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetHttpFallbackPolicy(
            HttpResponseMessage fallbackResponse,
            ILogger? logger = null)
        {
            return Policy<HttpResponseMessage>
                .Handle<Exception>()
                .OrResult(msg => !msg.IsSuccessStatusCode)
                .FallbackAsync(
                    fallbackResponse,
                    onFallbackAsync: (result, context) =>
                    {
                        logger?.LogWarning(
                            result.Exception,
                            "Fallback activated. Returning fallback response. OperationKey: {OperationKey}",
                            context.OperationKey ?? "Unknown");
                        return Task.CompletedTask;
                    });
        }

        /// <summary>
        /// Gets a combined policy with retry, circuit breaker, timeout, and bulkhead
        /// Layers: Fallback -> Timeout -> Retry -> Circuit Breaker -> Bulkhead
        /// </summary>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured policy wrap combining multiple policies</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetCombinedHttpPolicy(ILogger? logger = null)
        {
            var retryPolicy = GetHttpRetryPolicy(retryCount: 3, logger: logger);
            var circuitBreakerPolicy = GetHttpCircuitBreakerPolicy(
                failureThreshold: 0.5,
                samplingDuration: 10,
                minimumThroughput: 5,
                durationOfBreak: 30,
                logger: logger);
            var timeoutPolicy = GetHttpTimeoutPolicy(timeoutSeconds: 30, logger: logger);
            var bulkheadPolicy = GetHttpBulkheadPolicy(
                maxParallelization: 10,
                maxQueuingActions: 5,
                logger: logger);

            // Policy execution order: Timeout -> Retry -> Circuit Breaker -> Bulkhead
            return Policy.WrapAsync(timeoutPolicy, retryPolicy, circuitBreakerPolicy, bulkheadPolicy);
        }

        #endregion

        #region Database Policies

        /// <summary>
        /// Gets a retry policy for database operations with exponential backoff and jitter
        /// Handles transient database errors like deadlocks, timeouts, and connection issues
        /// </summary>
        /// <param name="retryCount">Number of retry attempts (default: 5)</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured retry policy</returns>
        public static IAsyncPolicy GetDatabaseRetryPolicy(int retryCount = 5, ILogger? logger = null)
        {
            return Policy
                .Handle<TimeoutException>()
                .Or<InvalidOperationException>(ex => ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
                .Or<System.Data.Common.DbException>() // Catch database-specific exceptions
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                                  + TimeSpan.FromMilliseconds(_jitterer.Next(0, 1000)),
                    onRetry: (exception, timespan, retryAttempt, context) =>
                    {
                        logger?.LogWarning(
                            exception,
                            "Database operation failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}. " +
                            "OperationKey: {OperationKey}",
                            retryAttempt, retryCount, timespan.TotalSeconds, exception.Message,
                            context.OperationKey ?? "Unknown");

                        context["RetryCount"] = retryAttempt;
                        context["LastError"] = exception.Message;
                    });
        }

        /// <summary>
        /// Gets a circuit breaker policy for database operations
        /// Prevents overwhelming the database with requests when it's failing
        /// </summary>
        /// <param name="failureThreshold">Failure rate before opening circuit (default: 0.7)</param>
        /// <param name="samplingDuration">Duration to monitor failures (default: 30 seconds)</param>
        /// <param name="minimumThroughput">Minimum requests before calculating failure rate (default: 10)</param>
        /// <param name="durationOfBreak">Duration to keep circuit open (default: 60 seconds)</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured circuit breaker policy</returns>
        public static IAsyncPolicy GetDatabaseCircuitBreakerPolicy(
            double failureThreshold = 0.7,
            int samplingDuration = 30,
            int minimumThroughput = 10,
            int durationOfBreak = 60,
            ILogger? logger = null)
        {
            return Policy
                .Handle<TimeoutException>()
                .Or<InvalidOperationException>(ex => ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
                .Or<System.Data.Common.DbException>()
                .AdvancedCircuitBreakerAsync(
                    failureThreshold: failureThreshold,
                    samplingDuration: TimeSpan.FromSeconds(samplingDuration),
                    minimumThroughput: minimumThroughput,
                    durationOfBreak: TimeSpan.FromSeconds(durationOfBreak),
                    onBreak: (exception, breakDelay, context) =>
                    {
                        logger?.LogError(
                            exception,
                            "Database circuit breaker OPENED for {BreakDuration}s. " +
                            "Failure threshold exceeded: {FailureThreshold}. " +
                            "Error: {ErrorMessage}. OperationKey: {OperationKey}",
                            breakDelay.TotalSeconds, failureThreshold, exception.Message,
                            context.OperationKey ?? "Unknown");
                    },
                    onReset: (context) =>
                    {
                        logger?.LogInformation(
                            "Database circuit breaker RESET. Database recovered. OperationKey: {OperationKey}",
                            context.OperationKey ?? "Unknown");
                    },
                    onHalfOpen: () =>
                    {
                        logger?.LogInformation(
                            "Database circuit breaker HALF-OPEN. Testing if database recovered.");
                    });
        }

        /// <summary>
        /// Gets a combined database policy with retry and circuit breaker
        /// </summary>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured policy wrap</returns>
        public static IAsyncPolicy GetCombinedDatabasePolicy(ILogger? logger = null)
        {
            var retryPolicy = GetDatabaseRetryPolicy(retryCount: 5, logger: logger);
            var circuitBreakerPolicy = GetDatabaseCircuitBreakerPolicy(
                failureThreshold: 0.7,
                samplingDuration: 30,
                minimumThroughput: 10,
                durationOfBreak: 60,
                logger: logger);

            // Policy execution order: Retry -> Circuit Breaker
            return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
        }

        #endregion

        #region Generic Policies

        /// <summary>
        /// Gets a generic retry policy for any operation with exponential backoff and jitter
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="retryCount">Number of retry attempts</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured retry policy</returns>
        public static IAsyncPolicy<T> GetGenericRetryPolicy<T>(int retryCount = 3, ILogger? logger = null)
        {
            return Policy<T>
                .Handle<Exception>(ex => !(ex is ArgumentNullException || ex is ArgumentException))
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                                  + TimeSpan.FromMilliseconds(_jitterer.Next(0, 1000)),
                    onRetry: (result, timespan, retryAttempt, context) =>
                    {
                        logger?.LogWarning(
                            result.Exception,
                            "Operation failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}. " +
                            "OperationKey: {OperationKey}",
                            retryAttempt, retryCount, timespan.TotalSeconds,
                            result.Exception?.Message ?? "Unknown",
                            context.OperationKey ?? "Unknown");
                    });
        }

        /// <summary>
        /// Gets a generic timeout policy for any operation
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="timeoutSeconds">Timeout in seconds</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Configured timeout policy</returns>
        public static IAsyncPolicy<T> GetGenericTimeoutPolicy<T>(int timeoutSeconds = 30, ILogger? logger = null)
        {
            return Policy.TimeoutAsync<T>(
                TimeSpan.FromSeconds(timeoutSeconds),
                TimeoutStrategy.Pessimistic,
                onTimeoutAsync: (context, timespan, task) =>
                {
                    logger?.LogWarning(
                        "Operation timed out after {TimeoutSeconds}s. OperationKey: {OperationKey}",
                        timespan.TotalSeconds, context.OperationKey ?? "Unknown");
                    return Task.CompletedTask;
                });
        }

        #endregion
    }
}
