namespace OrderProcessingSystem.Config
{
    /// <summary>
    /// Configuration settings for Redis cache
    /// </summary>
    public class RedisConfig
    {
        /// <summary>
        /// Connection string for Redis server
        /// </summary>
        public string ConnectionString { get; set; } = "localhost:6379";

        /// <summary>
        /// Default cache expiration time
        /// </summary>
        public TimeSpan DefaultCacheExpiration { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Sliding expiration time for frequently accessed data
        /// </summary>
        public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        public int ConnectTimeout { get; set; } = 5000;

        /// <summary>
        /// Sync timeout in milliseconds
        /// </summary>
        public int SyncTimeout { get; set; } = 5000;

        /// <summary>
        /// Async timeout in milliseconds
        /// </summary>
        public int AsyncTimeout { get; set; } = 5000;

        /// <summary>
        /// Number of connection retry attempts
        /// </summary>
        public int ConnectRetry { get; set; } = 3;

        /// <summary>
        /// Keep-alive interval in seconds
        /// </summary>
        public int KeepAlive { get; set; } = 60;

        /// <summary>
        /// Default database number
        /// </summary>
        public int DefaultDatabase { get; set; } = 0;

        /// <summary>
        /// Whether to abort connection on connect failure
        /// </summary>
        public bool AbortOnConnectFail { get; set; } = false;

        /// <summary>
        /// Instance name prefix for cache keys
        /// </summary>
        public string InstanceName { get; set; } = "OrderProcessing_";

        /// <summary>
        /// Enable Redis command logging in development
        /// </summary>
        public bool EnableCommandLogging { get; set; } = false;

        /// <summary>
        /// Retry policy exponential backoff base delay in milliseconds
        /// </summary>
        public int RetryPolicyBaseDelayMs { get; set; } = 5000;
    }

    /// <summary>
    /// Redis cache key prefixes for different entity types
    /// </summary>
    public static class RedisCacheKeys
    {
        public const string OrderPrefix = "order:";
        public const string OrderByNumberPrefix = "order:number:";
        public const string OrderListPrefix = "order:list:";
        public const string IdempotencyPrefix = "idempotency:";
        public const string CustomerPrefix = "customer:";
        public const string StatsPrefix = "stats:";
        public const string SessionPrefix = "session:";

        /// <summary>
        /// Generates a cache key for an order by ID
        /// </summary>
        public static string OrderById(int orderId) => $"{OrderPrefix}{orderId}";

        /// <summary>
        /// Generates a cache key for an order by order number
        /// </summary>
        public static string OrderByNumber(string orderNumber) => $"{OrderByNumberPrefix}{orderNumber}";

        /// <summary>
        /// Generates a cache key for an idempotency check
        /// </summary>
        public static string IdempotencyKey(string key) => $"{IdempotencyPrefix}{key}";

        /// <summary>
        /// Generates a cache key for customer data
        /// </summary>
        public static string CustomerById(string customerId) => $"{CustomerPrefix}{customerId}";

        /// <summary>
        /// Generates a cache key for order statistics
        /// </summary>
        public static string OrderStats(string statsType) => $"{StatsPrefix}orders:{statsType}";
    }

    /// <summary>
    /// Redis cache expiration policies
    /// </summary>
    public static class RedisCacheExpiration
    {
        /// <summary>
        /// Short-lived cache (5 minutes) - for frequently changing data
        /// </summary>
        public static readonly TimeSpan Short = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Medium-lived cache (30 minutes) - for moderately stable data
        /// </summary>
        public static readonly TimeSpan Medium = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Long-lived cache (1 hour) - for stable data
        /// </summary>
        public static readonly TimeSpan Long = TimeSpan.FromHours(1);

        /// <summary>
        /// Extended cache (6 hours) - for rarely changing data
        /// </summary>
        public static readonly TimeSpan Extended = TimeSpan.FromHours(6);

        /// <summary>
        /// Daily cache (24 hours) - for data that changes daily
        /// </summary>
        public static readonly TimeSpan Daily = TimeSpan.FromHours(24);

        /// <summary>
        /// Idempotency key expiration (24 hours) - standard for idempotency
        /// </summary>
        public static readonly TimeSpan Idempotency = TimeSpan.FromHours(24);

        /// <summary>
        /// Session cache (20 minutes) - for user session data
        /// </summary>
        public static readonly TimeSpan Session = TimeSpan.FromMinutes(20);
    }
}
