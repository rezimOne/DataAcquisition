namespace DataAcquisition.Worker.Tests;

/// <summary>Waiting helpers that do not depend on real time (no fixed delays).</summary>
internal static class TestWaits
{
    /// <summary>
    /// Never completes on its own; fails with <see cref="OperationCanceledException"/> once the token is canceled.
    /// Simulates a request that hangs until the caller gives up (host stopping, HttpClient timeout).
    /// </summary>
    public static Task UntilCanceledAsync(CancellationToken cancellationToken) =>
        new TaskCompletionSource().Task.WaitAsync(cancellationToken);
}
