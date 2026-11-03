namespace ProjektLabor.Services;

public abstract class AutomationBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(1);
    protected readonly TimeProvider TimeProvider;
    private readonly ILogger _logger;

    protected AutomationBackgroundService(TimeProvider timeProvider, ILogger logger)
    {
        TimeProvider = timeProvider;
        _logger = logger;
    }

    protected abstract string JobName { get; }
    protected abstract Task ProcessOnceAsync(CancellationToken cancellationToken);

    public Task RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunSafelyAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSafelyAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(PollingInterval, TimeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ProcessOnceAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Background job {JobName} failed; it will retry.", JobName);
        }
    }
}

public sealed class NoShowBackgroundService : AutomationBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public NoShowBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<NoShowBackgroundService> logger)
        : base(timeProvider, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override string JobName => "No-show";

    protected override async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<NoShowProcessor>();
        await processor.ProcessAsync(TimeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }
}

public sealed class BookingExpiryNotificationBackgroundService : AutomationBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BookingExpiryNotificationBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<BookingExpiryNotificationBackgroundService> logger)
        : base(timeProvider, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override string JobName => "Booking expiry notification";

    protected override async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<BookingExpiryNotificationProcessor>();
        await processor.ProcessAsync(TimeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }
}

public sealed class DailyKeyClosingBackgroundService : AutomationBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DailyKeyClosingBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<DailyKeyClosingBackgroundService> logger)
        : base(timeProvider, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override string JobName => "Daily key closing";

    protected override async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<DailyKeyClosingProcessor>();
        await processor.ProcessAsync(TimeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
