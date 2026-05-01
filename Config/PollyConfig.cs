using Polly;
using Polly.Extensions.Http;

namespace OrderProcessingSystem.Config
{
    /// <summary>
    /// Configuration for Polly resilience policies (retry, circuit breaker, timeout)
    /// </summary>
    public static class PollyConfig
    {
        /// <summary>
        /// Configures Polly policies for the application
        /// </summary>
        /// <param name="services">Service collection</param>
        public static void ConfigurePolicies(IServiceCollection services)
        {
            // Add HTTP client with Polly policies if needed
            // This is a placeholder for configuring policies at the service level
            // Individual services can use the static methods to get specific policies
        }

        /// <summary>
        /// Gets a retry policy for HTTP requests with exponential backoff
        /// </summary>
        /// <param name="retryCount">Number of retry attempts (default: 3)</param>
        /// <returns>Configured retry policy</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(int retryCount = 3)
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        Console.WriteLine($"Retry {retryCount} after {timespan.TotalSeconds}s due to {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                    });
        }

        /// <summary>
        /// Gets a circuit breaker policy to prevent cascading failures
        /// </summary>
        /// <param name="exceptionsAllowedBeforeBreaking">Number of exceptions before opening circuit (default: 5)</param>
        /// <param name="durationOfBreak">Duration to keep circuit open (default: 30 seconds)</param>
        /// <returns>Configured circuit breaker policy</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
            int exceptionsAllowedBeforeBreaking = 5,
            int durationOfBreak = 30)
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking,
                    TimeSpan.FromSeconds(durationOfBreak),
                    onBreak: (result, duration) =>
                    {
                        Console.WriteLine($"Circuit breaker opened for {duration.TotalSeconds}s due to {result.Exception?.Message ?? result.Result.StatusCode.ToString()}");
                    },
                    onReset: () =>
                    {
                        Console.WriteLine("Circuit breaker reset");
                    });
        }

        /// <summary>
        /// Gets a timeout policy to prevent long-running requests
        /// </summary>
        /// <param name="timeoutSeconds">Timeout in seconds (default: 10)</param>
        /// <returns>Configured timeout policy</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy(int timeoutSeconds = 10)
        {
            return Policy.TimeoutAsync<HttpResponseMessage>(
                TimeSpan.FromSeconds(timeoutSeconds),
                onTimeoutAsync: (context, timespan, task) =>
                {
                    Console.WriteLine($"Request timed out after {timespan.TotalSeconds}s");
                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Gets a combined policy with retry, circuit breaker, and timeout
        /// </summary>
        /// <returns>Configured policy wrap combining multiple policies</returns>
        public static IAsyncPolicy<HttpResponseMessage> GetCombinedPolicy()
        {
            var retryPolicy = GetRetryPolicy();
            var circuitBreakerPolicy = GetCircuitBreakerPolicy();
            var timeoutPolicy = GetTimeoutPolicy();

            // Combine policies: timeout -> retry -> circuit breaker
            return Policy.WrapAsync(timeoutPolicy, retryPolicy, circuitBreakerPolicy);
        }
    }
}
