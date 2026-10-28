## Logging & Observability

The API implements structured logging using the built-in ASP.NET Core `ILogger<T>` abstraction and JSON Console Logger.

### Logging Strategy

- **Structured logging:** Application and HTTP events are recorded using message templates and named properties.
- **Environment-based configuration:** Log levels are configured through `appsettings.json` and `appsettings.Development.json`.
- **Request correlation:** Each HTTP request uses `HttpContext.TraceIdentifier` as its correlation identifier.
- **Logging scopes:** The correlation ID is propagated through the request execution context using `ILogger.BeginScope()`.
- **Request completion logging:** Custom middleware records the HTTP method, request path, response status code, and elapsed processing time.
- **Centralized exception logging:** Unhandled exceptions are recorded by the global exception handler, while clients receive standardized HTTP 500 responses without internal exception details.

### Business Event Logging

The application records meaningful business events at appropriate logging levels.

| Level | Events |
|---|---|
| Information | Order creation, status updates, successful cancellation, idempotent replay |
| Warning | Insufficient stock, idempotency conflict, invalid status transition, detected concurrency conflict |
| Error | Unexpected exceptions and unhandled request failures |
| Debug | Optional application diagnostics |

Business success events are logged only after the corresponding database transaction has been successfully committed.

### Correlation ID

Every HTTP response includes an `X-Correlation-ID` header.

The same identifier is included in the structured logging scope, allowing HTTP request logs, application events, and exception logs to be correlated.

For example, an operator can use the correlation ID from a failed HTTP response to locate the corresponding request completion log and business rejection event.

### Data Safety

Application logging avoids recording sensitive request payloads, credentials, connection strings, and shipping addresses. Exception details are recorded server-side rather than returned in API error responses.

### Scope and Limitations

The current implementation uses structured console logging and is designed for request-level tracing within a single API service.

Centralized log storage, distributed tracing, metrics collection, and monitoring dashboards are outside the scope of this prototype. These capabilities can be introduced if operational requirements expand.