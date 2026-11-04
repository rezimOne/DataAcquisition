using DataAcquisition.Api.Observations;
using DataAcquisition.Contracts;
using Xunit;
using static DataAcquisition.Api.Tests.TestData;

namespace DataAcquisition.Api.Tests;

public sealed class ObservationBatchValidatorTests
{
    [Fact]
    public void Validate_ValidBatchWithSampleAndCondition_ReturnsNoErrors()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1), Condition(2)));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_UnavailableValue_ReturnsNoErrors()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1, value: "UNAVAILABLE")));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MaxBatchSize_ReturnsNoErrors()
    {
        var observations = Enumerable.Range(1, ApiContract.MaxBatchSize).Select(i => Sample(i)).ToArray();

        var errors = ObservationBatchValidator.Validate(Batch(observations));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingDeviceId_ReturnsDeviceIdError(string? deviceId)
    {
        var errors = ObservationBatchValidator.Validate(Batch(deviceId!, InstanceId, Sample(1)));

        Assert.Contains("deviceId", errors.Keys);
    }

    [Fact]
    public void Validate_TooLongDeviceId_ReturnsDeviceIdError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(new string('d', 65), InstanceId, Sample(1)));

        Assert.Contains("deviceId", errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Validate_InvalidInstanceId_ReturnsInstanceIdError(long? instanceId)
    {
        var errors = ObservationBatchValidator.Validate(new ObservationBatchRequest(DeviceId, instanceId, [Sample(1)]));

        Assert.Contains("instanceId", errors.Keys);
    }

    [Fact]
    public void Validate_NullObservations_ReturnsObservationsError()
    {
        var errors = ObservationBatchValidator.Validate(new ObservationBatchRequest(DeviceId, InstanceId, null));

        Assert.Contains("observations", errors.Keys);
    }

    [Fact]
    public void Validate_EmptyObservations_ReturnsObservationsError()
    {
        var errors = ObservationBatchValidator.Validate(Batch());

        Assert.Contains("observations", errors.Keys);
    }

    [Fact]
    public void Validate_MoreThanMaxBatchSize_ReturnsObservationsError()
    {
        var observations = Enumerable.Range(1, ApiContract.MaxBatchSize + 1).Select(i => Sample(i)).ToArray();

        var errors = ObservationBatchValidator.Validate(Batch(observations));

        Assert.Contains("observations", errors.Keys);
    }

    [Fact]
    public void Validate_NullObservationElement_ReturnsIndexedError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1), null));

        Assert.Contains("observations[1]", errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingDataItemId_ReturnsIndexedFieldError(string? dataItemId)
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1), Sample(2) with { DataItemId = dataItemId }));

        Assert.Equal(["observations[1].dataItemId"], errors.Keys);
    }

    [Fact]
    public void Validate_TooLongDataItemId_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1) with { DataItemId = new string('x', 65) }));

        Assert.Equal(["observations[0].dataItemId"], errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void Validate_InvalidSequence_ReturnsIndexedFieldError(long? sequence)
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1) with { Sequence = sequence }));

        Assert.Equal(["observations[0].sequence"], errors.Keys);
    }

    [Fact]
    public void Validate_DuplicateSequenceInBatch_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1), Sample(2), Sample(1, dataItemId: "ypos")));

        Assert.Equal(["observations[2].sequence"], errors.Keys);
    }

    [Fact]
    public void Validate_MissingTimestamp_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1) with { Timestamp = null }));

        Assert.Equal(["observations[0].timestamp"], errors.Keys);
    }

    [Fact]
    public void Validate_MissingCategory_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1) with { Category = null }));

        Assert.Contains("observations[0].category", errors.Keys);
    }

    [Fact]
    public void Validate_UndefinedCategory_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1) with { Category = (ObservationCategory)7 }));

        Assert.Equal(["observations[0].category"], errors.Keys);
    }

    [Fact]
    public void Validate_UndefinedConditionLevel_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Condition(1) with { ConditionLevel = (ConditionLevel)42 }));

        Assert.Equal(["observations[0].conditionLevel"], errors.Keys);
    }

    [Fact]
    public void Validate_TooLongTypeAndSubType_ReturnsIndexedFieldErrors()
    {
        var errors = ObservationBatchValidator.Validate(
            Batch(Sample(1) with { Type = new string('t', 65), SubType = new string('s', 65) }));

        Assert.Equal(["observations[0].type", "observations[0].subType"], errors.Keys);
    }

    [Fact]
    public void Validate_MissingValue_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1) with { Value = null }));

        Assert.Equal(["observations[0].value"], errors.Keys);
    }

    [Fact]
    public void Validate_TooLongValue_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Sample(1) with { Value = new string('v', 1025) }));

        Assert.Equal(["observations[0].value"], errors.Keys);
    }

    [Fact]
    public void Validate_ConditionWithoutLevel_ReturnsIndexedFieldError()
    {
        var errors = ObservationBatchValidator.Validate(Batch(Condition(1) with { ConditionLevel = null }));

        Assert.Equal(["observations[0].conditionLevel"], errors.Keys);
    }

    [Theory]
    [InlineData(ObservationCategory.Sample)]
    [InlineData(ObservationCategory.Event)]
    public void Validate_NonConditionWithLevel_ReturnsIndexedFieldError(ObservationCategory category)
    {
        var errors = ObservationBatchValidator.Validate(
            Batch(Sample(1) with { Category = category, ConditionLevel = ConditionLevel.Normal }));

        Assert.Equal(["observations[0].conditionLevel"], errors.Keys);
    }
}
