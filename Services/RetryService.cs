using Polly;
using Polly.Retry;

namespace OrderProcessingSystem.Services
{
    /// <summary>
    /// Service for handling retry logic with exponential backoff
    /// </summary>
    public class RetryService
    {
        private readonly ILogger<RetryService> _logger;

        public RetryService(ILogger<RetryService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes an async operation with retry logic
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <returns>Result of the operation</returns>
        public async Task<TResult> ExecuteWithRetryAsync<TResult>(
            Func<Task<TResult>> operation,
            int retryCount = 3,
            string operationName = "Operation")
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retry, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "{OperationName} failed (attempt {RetryCount}/{MaxRetries}). Retrying in {DelaySeconds}s...",
                            operationName, retry, retryCount, timeSpan.TotalSeconds);
                    });

            try
            {
                return await retryPolicy.ExecuteAsync(operation);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{OperationName} failed after {RetryCount} retry attempts",
                    operationName, retryCount);
                throw;
            }
        }

        /// <summary>
        /// Executes an async operation without return value with retry logic
        /// </summary>
        /// <param name="operation">The operation to execute</param>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <param name="operationName">Name of the operation for logging</param>
        public async Task ExecuteWithRetryAsync(
            Func<Task> operation,
            int retryCount = 3,
            string operationName = "Operation")
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retry, context) =>
                    {
                        _logger.LogWarning(
                            exception,
                            "{OperationName} failed (attempt {RetryCount}/{MaxRetries}). Retrying in {DelaySeconds}s...",
                            operationName, retry, retryCount, timeSpan.TotalSeconds);
                    });

            try
            {
                await retryPolicy.ExecuteAsync(operation);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{OperationName} failed after {RetryCount} retry attempts",
                    operationName, retryCount);
                throw;
            }
        }

        /// <summary>
        /// Executes an async operation with custom retry policy
        /// </summary>
        /// <typeparam name="TResult">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute</param>
        /// <param name="policy">Custom retry policy</param>
        /// <returns>Result of the operation</returns>
        public async Task<TResult> ExecuteWithCustomPolicyAsync<TResult>(
            Func<Task<TResult>> operation,
            AsyncRetryPolicy policy)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            return await policy.ExecuteAsync(operation);
        }
    }
}
