using System.Diagnostics;

namespace AiLifeOS.Api.Services;

public sealed class AiRequestGate(ILogger<AiRequestGate> logger)
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async ValueTask<IAsyncDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await semaphore.WaitAsync(cancellationToken);
        stopwatch.Stop();

        if (stopwatch.Elapsed >= TimeSpan.FromSeconds(1))
            logger.LogInformation("AI provider request waited {WaitMilliseconds} ms for another request to finish.",
                stopwatch.ElapsedMilliseconds);

        return new Lease(semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
