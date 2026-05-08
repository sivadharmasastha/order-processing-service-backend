using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Polly.Wrap;
using System.Diagnostics;

namespace OrderProcessingSystem.Services
{
    /// <summary>
    /// Production-grade retry service with comprehensive resilience patterns:
    /// - Exponential backoff with jitter
    /// - Circuit breaker pattern
    /// - Timeout handling
    /// - Fallback mechanisms
    /// - Detailed metrics and diagnostics
    /// </summary>
    public class RetryService
    {
        private readonly ILogger<RetryService> _logger;
        private static readonly Random _jitterer = new Random();

        public RetryService(ILogger<RetryService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Basic Retry Operations

        /// <summary>
        /// Executes an async operation with retry logic using exponential backoff and jitter
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public async Task<TResult> ExecuteWithRetryAsync<TResult>(
            Func<Task<TResult>> operation,
            int retryCount = 3,
            string operationName = "Operation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var stopwatch = Stopwatch.StartNew();
            var retryPolicy = Policy
                .Handle<Exception>(ex => !IsNonRetriableException(ex))
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => CalculateRetryDelay(retryAttempt),
                    onRetry: (exception, timeSpan, retryAttempt, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "{OperationName} failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}. Elapsed: {ElapsedMs}ms",
                            operationName, retryAttempt, retryCount, timeSpan.TotalSeconds,
                            exception.Message, stopwatch.ElapsedMilliseconds);

                        context["RetryAttempt"] = retryAttempt;
                        context["ElapsedMs"] = stopwatch.ElapsedMilliseconds;
                    });

            try
            {
                var result = await retryPolicy.ExecuteAsync(ct => operation(), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed successfully in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} failed after {RetryCount} retry attempts. Total elapsed: {ElapsedMs}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Executes an async operation without return value with retry logic
        /// </summary>
        /// <param name="operation">The operation to execute</param>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task ExecuteWithRetryAsync(
            Func<Task> operation,
            int retryCount = 3,
            string operationName = "Operation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var stopwatch = Stopwatch.StartNew();
            var retryPolicy = Policy
                .Handle<Exception>(ex => !IsNonRetriableException(ex))
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => CalculateRetryDelay(retryAttempt),
                    onRetry: (exception, timeSpan, retryAttempt, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "{OperationName} failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}. Elapsed: {ElapsedMs}ms",
                            operationName, retryAttempt, retryCount, timeSpan.TotalSeconds,
                            exception.Message, stopwatch.ElapsedMilliseconds);
                    });

            try
            {
                await retryPolicy.ExecuteAsync(ct => operation(), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed successfully in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} failed after {RetryCount} retry attempts. Total elapsed: {ElapsedMs}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        #endregion

        #region Advanced Retry with Circuit Breaker

        /// <summary>
        /// Executes an operation with retry and circuit breaker pattern
        /// Circuit breaker prevents cascade failures by stopping retries when service is down
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="failureThreshold">Failure rate to open circuit (default: 0.5)</param>
        /// <param name="samplingDuration">Duration to monitor failures (default: 30 seconds)</param>
        /// <param name="circuitBreakDuration">Duration to keep circuit open (default: 60 seconds)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public async Task<TResult> ExecuteWithRetryAndCircuitBreakerAsync<TResult>(
            Func<Task<TResult>> operation,
            int retryCount = 3,
            double failureThreshold = 0.5,
            int samplingDuration = 30,
            int circuitBreakDuration = 60,
            string operationName = "Operation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var stopwatch = Stopwatch.StartNew();

            // Retry policy with exponential backoff and jitter
            var retryPolicy = Policy<TResult>
                .Handle<Exception>(ex => !IsNonRetriableException(ex) && !(ex is BrokenCircuitException))
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => CalculateRetryDelay(retryAttempt),
                    onRetry: (outcome, timeSpan, retryAttempt, context) =>
                    {
                        _logger.LogWarning(
                            outcome.Exception,
                            "{OperationName} failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}",
                            operationName, retryAttempt, retryCount, timeSpan.TotalSeconds,
                            outcome.Exception?.Message ?? "Unknown");
                    });

            // Circuit breaker policy
            var circuitBreakerPolicy = Policy<TResult>
                .Handle<Exception>(ex => !IsNonRetriableException(ex))
                .AdvancedCircuitBreakerAsync(
                    failureThreshold: failureThreshold,
                    samplingDuration: TimeSpan.FromSeconds(samplingDuration),
                    minimumThroughput: 5,
                    durationOfBreak: TimeSpan.FromSeconds(circuitBreakDuration),
                    onBreak: (outcome, duration, context) =>
                    {
                        _logger.LogError(
                            outcome.Exception,
                            "{OperationName} circuit breaker OPENED for {BreakDuration}s. " +
                            "Failure threshold: {FailureThreshold}. Error: {ErrorMessage}",
                            operationName, duration.TotalSeconds, failureThreshold,
                            outcome.Exception?.Message ?? "Unknown");
                    },
                    onReset: (context) =>
                    {
                        _logger.LogInformation(
                            "{OperationName} circuit breaker RESET. Service recovered.",
                            operationName);
                    },
                    onHalfOpen: () =>
                    {
                        _logger.LogInformation(
                            "{OperationName} circuit breaker HALF-OPEN. Testing service recovery.",
                            operationName);
                    });

            // Combine policies: Retry -> Circuit Breaker
            var combinedPolicy = Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);

            try
            {
                var result = await combinedPolicy.ExecuteAsync(ct => operation(), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed successfully in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (BrokenCircuitException ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} failed due to open circuit breaker. Total elapsed: {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException(
                    $"{operationName} is temporarily unavailable due to repeated failures. Please try again later.",
                    ex);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} failed after {RetryCount} retry attempts. Total elapsed: {ElapsedMs}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        #endregion

        #region Retry with Timeout

        /// <summary>
        /// Executes an operation with retry and timeout policies
        /// Ensures operations don't run indefinitely
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="timeoutSeconds">Timeout per attempt in seconds (default: 30)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public async Task<TResult> ExecuteWithRetryAndTimeoutAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            int retryCount = 3,
            int timeoutSeconds = 30,
            string operationName = "Operation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var stopwatch = Stopwatch.StartNew();

            // Timeout policy
            var timeoutPolicy = Policy.TimeoutAsync<TResult>(
                TimeSpan.FromSeconds(timeoutSeconds),
                TimeoutStrategy.Pessimistic,
                onTimeoutAsync: (context, timespan, task) =>
                {
                    _logger.LogWarning(
                        "{OperationName} timed out after {TimeoutSeconds}s",
                        operationName, timespan.TotalSeconds);
                    return Task.CompletedTask;
                });

            // Retry policy
            var retryPolicy = Policy<TResult>
                .Handle<Exception>(ex => !IsNonRetriableException(ex))
                .Or<TimeoutRejectedException>()
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => CalculateRetryDelay(retryAttempt),
                    onRetry: (outcome, timeSpan, retryAttempt, context) =>
                    {
                        _logger.LogWarning(
                            outcome.Exception,
                            "{OperationName} failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}",
                            operationName, retryAttempt, retryCount, timeSpan.TotalSeconds,
                            outcome.Exception?.Message ?? "Unknown");
                    });

            // Combine policies: Timeout -> Retry
            var combinedPolicy = Policy.WrapAsync(timeoutPolicy, retryPolicy);

            try
            {
                var result = await combinedPolicy.ExecuteAsync(ct => operation(ct), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed successfully in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (TimeoutRejectedException ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} timed out after {RetryCount} retry attempts. Total elapsed: {ElapsedMs}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
                throw new TimeoutException(
                    $"{operationName} exceeded the allowed timeout of {timeoutSeconds}s per attempt.",
                    ex);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} failed after {RetryCount} retry attempts. Total elapsed: {ElapsedMs}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        #endregion

        #region Retry with Fallback

        /// <summary>
        /// Executes an operation with retry and fallback mechanisms
        /// Returns fallback value if all retries fail
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="fallbackValue">Fallback value to return on failure</param>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation or fallback value</returns>
        public async Task<TResult> ExecuteWithRetryAndFallbackAsync<TResult>(
            Func<Task<TResult>> operation,
            TResult fallbackValue,
            int retryCount = 3,
            string operationName = "Operation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var stopwatch = Stopwatch.StartNew();

            // Retry policy
            var retryPolicy = Policy<TResult>
                .Handle<Exception>(ex => !IsNonRetriableException(ex))
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => CalculateRetryDelay(retryAttempt),
                    onRetry: (outcome, timeSpan, retryAttempt, context) =>
                    {
                        _logger.LogWarning(
                            outcome.Exception,
                            "{OperationName} failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}",
                            operationName, retryAttempt, retryCount, timeSpan.TotalSeconds,
                            outcome.Exception?.Message ?? "Unknown");
                    });

            try
            {
                var result = await retryPolicy.ExecuteAsync(ct => operation(), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    ex,
                    "{OperationName} failed after {RetryCount} retries. Returning fallback value. Total elapsed: {ElapsedMs}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
                
                return fallbackValue;
            }
        }

        /// <summary>
        /// Executes an operation with retry and fallback function
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="fallbackOperation">Fallback operation to execute on failure</param>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation or fallback operation</returns>
        public async Task<TResult> ExecuteWithRetryAndFallbackAsync<TResult>(
            Func<Task<TResult>> operation,
            Func<Task<TResult>> fallbackOperation,
            int retryCount = 3,
            string operationName = "Operation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (fallbackOperation == null)
            {
                throw new ArgumentNullException(nameof(fallbackOperation));
            }

            var stopwatch = Stopwatch.StartNew();

            // Retry policy
            var retryPolicy = Policy<TResult>
                .Handle<Exception>(ex => !IsNonRetriableException(ex))
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => CalculateRetryDelay(retryAttempt),
                    onRetry: (outcome, timeSpan, retryAttempt, context) =>
                    {
                        _logger.LogWarning(
                            outcome.Exception,
                            "{OperationName} failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Error: {ErrorMessage}",
                            operationName, retryAttempt, retryCount, timeSpan.TotalSeconds,
                            outcome.Exception?.Message ?? "Unknown");
                    });

            try
            {
                var result = await retryPolicy.ExecuteAsync(ct => operation(), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    ex,
                    "{OperationName} failed after {RetryCount} retries. Executing fallback operation. Total elapsed: {ElapsedMs}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
                
                try
                {
                    var fallbackResult = await fallbackOperation();
                    _logger.LogInformation(
                        "{OperationName} fallback operation completed successfully",
                        operationName);
                    return fallbackResult;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(
                        fallbackEx,
                        "{OperationName} fallback operation also failed",
                        operationName);
                    throw;
                }
            }
        }

        #endregion

        #region Custom Policy Execution

        /// <summary>
        /// Executes an async operation with a custom Polly policy
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="policy">Custom Polly policy</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public async Task<TResult> ExecuteWithCustomPolicyAsync<TResult>(
            Func<Task<TResult>> operation,
            IAsyncPolicy<TResult> policy,
            string operationName = "Operation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await policy.ExecuteAsync(ct => operation(), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed successfully in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} failed. Total elapsed: {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Executes an async operation without return value with a custom Polly policy
        /// </summary>
        /// <param name="operation">The operation to execute</param>
        /// <param name="policy">Custom Polly policy</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task ExecuteWithCustomPolicyAsync(
            Func<Task> operation,
            IAsyncPolicy policy,
            string operationName = "Operation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                await policy.ExecuteAsync(ct => operation(), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed successfully in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} failed. Total elapsed: {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        #endregion

        #region Database-Specific Operations

        /// <summary>
        /// Executes a database operation with retry logic optimized for transient database errors
        /// Handles deadlocks, timeouts, and connection issues
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The database operation to execute</param>
        /// <param name="retryCount">Number of retry attempts (default: 5)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public async Task<TResult> ExecuteDatabaseOperationAsync<TResult>(
            Func<Task<TResult>> operation,
            int retryCount = 5,
            string operationName = "DatabaseOperation",
            CancellationToken cancellationToken = default)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var stopwatch = Stopwatch.StartNew();
            var retryPolicy = Policy<TResult>
                .Handle<TimeoutException>()
                .Or<InvalidOperationException>(ex => ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
                .Or<System.Data.Common.DbException>()
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => CalculateRetryDelay(retryAttempt),
                    onRetry: (outcome, timeSpan, retryAttempt, context) =>
                    {
                        _logger.LogWarning(
                            outcome.Exception,
                            "{OperationName} failed (attempt {RetryAttempt}/{MaxRetries}). " +
                            "Retrying in {DelaySeconds}s. Database error: {ErrorMessage}. Elapsed: {ElapsedMs}ms",
                            operationName, retryAttempt, retryCount, timeSpan.TotalSeconds,
                            outcome.Exception?.Message ?? "Unknown", stopwatch.ElapsedMilliseconds);
                    });

            try
            {
                var result = await retryPolicy.ExecuteAsync(ct => operation(), cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "{OperationName} completed successfully in {ElapsedMs}ms",
                    operationName, stopwatch.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "{OperationName} failed after {RetryCount} retry attempts. Total elapsed: {ElapsedMs}ms",
                    operationName, retryCount, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Calculates retry delay with exponential backoff and jitter
        /// Jitter prevents thundering herd problem when multiple clients retry simultaneously
        /// </summary>
        /// <param name="retryAttempt">Current retry attempt number</param>
        /// <returns>Delay before next retry</returns>
        private static TimeSpan CalculateRetryDelay(int retryAttempt)
        {
            // Exponential backoff: 2^retryAttempt seconds
            var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));

            // Add jitter: random value between 0 and 1000ms
            var jitter = TimeSpan.FromMilliseconds(_jitterer.Next(0, 1000));

            return exponentialDelay + jitter;
        }

        /// <summary>
        /// Determines if an exception should not be retried
        /// Non-retriable exceptions are typically programming errors or validation failures
        /// </summary>
        /// <param name="exception">Exception to check</param>
        /// <returns>True if exception should not be retried</returns>
        private static bool IsNonRetriableException(Exception exception)
        {
            return exception is ArgumentNullException ||
                   exception is ArgumentException ||
                   exception is InvalidOperationException && 
                   !exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                   exception is NotSupportedException ||
                   exception is NotImplementedException;
        }

        #endregion
    }
}
