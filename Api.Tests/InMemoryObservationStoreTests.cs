using DataAcquisition.Api.Observations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;
using static DataAcquisition.Api.Tests.TestData;

namespace DataAcquisition.Api.Tests;

public sealed class InMemoryObservationStoreTests
{
    private readonly FakeLogger<InMemoryObservationStore> _logger = new();
    private readonly InMemoryObservationStore _store;

    public InMemoryObservationStoreTests() => _store = new InMemoryObservationStore(_logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddAsync_NewObservations_AcceptsAll()
    {
        var result = await _store.AddAsync([Stored(1), Stored(2)], Ct);

        Assert.Equal(2, result.Accepted);
        Assert.Equal(0, result.Duplicates);
    }

    [Fact]
    public async Task AddAsync_SameKeysTwice_CountsDuplicatesAndKeepsFirst()
    {
        await _store.AddAsync([Stored(1), Stored(2)], Ct);

        var result = await _store.AddAsync([Stored(1), Stored(2, value: "999.000"), Stored(3)], Ct);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(2, result.Duplicates);
        var stored = await _store.QueryAsync(new ObservationQuery(DeviceId, null, null, 10), Ct);
        Assert.Equal([1L, 2L, 3L], stored.Items.Select(o => o.Sequence));
        Assert.Equal("112.457", stored.Items[1].Value);
    }

    [Fact]
    public async Task AddAsync_DuplicateWithDifferentContent_LogsWarningWithoutValue()
    {
        await _store.AddAsync([Stored(1)], Ct);

        await _store.AddAsync([Stored(1, value: "999.000")], Ct);

        var warning = Assert.Single(_logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
        Assert.DoesNotContain("999.000", warning.Message);
    }

    [Fact]
    public async Task AddAsync_SameSequenceInOtherInstanceOrDevice_AcceptsAsDistinct()
    {
        await _store.AddAsync([Stored(1)], Ct);

        var result = await _store.AddAsync([Stored(1, instanceId: InstanceId + 1), Stored(1, deviceId: "OTHER")], Ct);

        Assert.Equal(2, result.Accepted);
        Assert.Equal(0, result.Duplicates);
    }

    [Fact]
    public async Task AddAsync_ConcurrentIdenticalBatches_StoresEachObservationOnce()
    {
        var batch = Enumerable.Range(1, 500).Select(i => Stored(i)).ToList();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => _store.AddAsync(batch, Ct), Ct)));

        Assert.Equal(500, results.Sum(r => r.Accepted));
        Assert.Equal(7 * 500, results.Sum(r => r.Duplicates));
        var stored = await _store.QueryAsync(new ObservationQuery(DeviceId, null, null, 1000), Ct);
        Assert.Equal(500, stored.Items.Count);
    }

    [Fact]
    public async Task QueryAsync_MultipleDevicesAndInstances_FiltersByDeviceAndSortsByInstanceThenSequence()
    {
        await _store.AddAsync(
            [Stored(5, instanceId: InstanceId + 1), Stored(2), Stored(1, deviceId: "OTHER"), Stored(1), Stored(3, instanceId: InstanceId + 1)],
            Ct);

        var result = await _store.QueryAsync(new ObservationQuery(DeviceId, null, null, 10), Ct);

        Assert.Equal(
            [(InstanceId, 1L), (InstanceId, 2L), (InstanceId + 1, 3L), (InstanceId + 1, 5L)],
            result.Items.Select(o => (o.InstanceId, o.Sequence)));
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task QueryAsync_TimeRange_IncludesFromAndExcludesTo()
    {
        await _store.AddAsync([Stored(1), Stored(2), Stored(3), Stored(4)], Ct);

        var result = await _store.QueryAsync(
            new ObservationQuery(DeviceId, Stored(2).Timestamp, Stored(4).Timestamp, 10), Ct);

        Assert.Equal([2L, 3L], result.Items.Select(o => o.Sequence));
    }

    [Fact]
    public async Task QueryAsync_MoreMatchesThanLimit_ReturnsLimitAndTruncated()
    {
        await _store.AddAsync([Stored(1), Stored(2), Stored(3)], Ct);

        var result = await _store.QueryAsync(new ObservationQuery(DeviceId, null, null, 2), Ct);

        Assert.Equal([1L, 2L], result.Items.Select(o => o.Sequence));
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task QueryAsync_ExactlyLimitMatches_IsNotTruncated()
    {
        await _store.AddAsync([Stored(1), Stored(2)], Ct);

        var result = await _store.QueryAsync(new ObservationQuery(DeviceId, null, null, 2), Ct);

        Assert.Equal(2, result.Items.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task QueryAsync_UnknownDevice_ReturnsEmpty()
    {
        await _store.AddAsync([Stored(1)], Ct);

        var result = await _store.QueryAsync(new ObservationQuery("UNKNOWN", null, null, 10), Ct);

        Assert.Empty(result.Items);
        Assert.False(result.Truncated);
    }
}
