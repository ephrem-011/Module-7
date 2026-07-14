using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Generate an 8-character short correlation ID
        string correlationId = Guid.NewGuid().ToString("N")[..8];

        // 2. Attach the correlation ID to the response headers before calling next
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        // 3. Log the entry line
        _logger.LogInformation("Entry: {Method} {Path} [CorrelationID: {CorrelationId}]", 
            context.Request.Method, 
            context.Request.Path, 
            correlationId);

        // 4. Start the stopwatch to measure execution time
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            // 5. Stop the stopwatch and log the exit line
            stopwatch.Stop();
            
            _logger.LogInformation("Exit: {StatusCode} in {ElapsedMs}ms [CorrelationID: {CorrelationId}]", 
                context.Response.StatusCode, 
                stopwatch.ElapsedMilliseconds, 
                correlationId);
        }
    }
}
