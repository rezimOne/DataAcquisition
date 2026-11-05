using DataAcquisition.Contracts;
using DataAcquisition.Worker.Api;
using DataAcquisition.Worker.MTConnect;
using Xunit;
using WorkerCategory = DataAcquisition.Worker.MTConnect.ObservationCategory;
using WorkerConditionLevel = DataAcquisition.Worker.MTConnect.ConditionLevel;

namespace DataAcquisition.Worker.Tests.Api;

public sealed class ObservationBatchMappingTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 4, 10, 15, 30, 120, TimeSpan.Zero);

    [Fact]
    public void ToRequest_SampleAndCondition_MapsEveryField()
    {
        var batch = new ObservationBatch("VMC5AX", 1793790000, ObservationSource.Sample,
        [
            new MTConnectObservation("VMC5AX", 1793790000, "xpos", 41, Time, WorkerCategory.Sample,
                "POSITION", "ACTUAL", "MILLIMETER", "112.457", null, null),
            new MTConnectObservation("VMC5AX", 1793790000, "system", 42, Time, WorkerCategory.Condition,
                "SYSTEM", null, null, "SPINDLE OVERLOAD", WorkerConditionLevel.Fault, "AL-0123"),
        ]);

        var request = batch.ToRequest();

        Assert.Equal(("VMC5AX", (long?)1793790000), (request.DeviceId, request.InstanceId));
        Assert.Equal(
            new ObservationDto("xpos", 41, Time, Contracts.ObservationCategory.Sample, "POSITION", "ACTUAL", "112.457", null)
            {
                Units = "MILLIMETER",
            },
            request.Observations![0]);
        Assert.Equal(
            new ObservationDto("system", 42, Time, Contracts.ObservationCategory.Condition, "SYSTEM", null, "SPINDLE OVERLOAD",
                Contracts.ConditionLevel.Fault)
            {
                NativeCode = "AL-0123",
            },
            request.Observations[1]);
    }

    [Theory]
    [InlineData(WorkerCategory.Sample, Contracts.ObservationCategory.Sample)]
    [InlineData(WorkerCategory.Event, Contracts.ObservationCategory.Event)]
    [InlineData(WorkerCategory.Condition, Contracts.ObservationCategory.Condition)]
    public void ToRequest_EveryCategory_MapsByName(WorkerCategory category, Contracts.ObservationCategory expected)
    {
        var batch = new ObservationBatch("D", 1, ObservationSource.Current,
            [new MTConnectObservation("D", 1, "x", 1, Time, category, null, null, null, "v", null, null)]);

        Assert.Equal(expected, batch.ToRequest().Observations![0]!.Category);
    }

    [Theory]
    [InlineData(WorkerConditionLevel.Normal, Contracts.ConditionLevel.Normal)]
    [InlineData(WorkerConditionLevel.Warning, Contracts.ConditionLevel.Warning)]
    [InlineData(WorkerConditionLevel.Fault, Contracts.ConditionLevel.Fault)]
    [InlineData(WorkerConditionLevel.Unavailable, Contracts.ConditionLevel.Unavailable)]
    public void ToRequest_EveryConditionLevel_MapsByName(WorkerConditionLevel level, Contracts.ConditionLevel expected)
    {
        var batch = new ObservationBatch("D", 1, ObservationSource.Current,
            [new MTConnectObservation("D", 1, "s", 1, Time, WorkerCategory.Condition, "SYSTEM", null, null, "", level, null)]);

        Assert.Equal(expected, batch.ToRequest().Observations![0]!.ConditionLevel);
    }
}
