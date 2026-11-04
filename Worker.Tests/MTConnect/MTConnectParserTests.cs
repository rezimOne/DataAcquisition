using DataAcquisition.Worker.MTConnect;
using Xunit;

namespace DataAcquisition.Worker.Tests.MTConnect;

public sealed class MTConnectParserTests
{
    [Fact]
    public void ParseDevices_SimulatorProbe_ReturnsDeviceAndDataItems()
    {
        var devices = MTConnectParser.ParseDevices(Fixtures.Read("probe.xml"));

        Assert.Equal(Fixtures.InstanceId, devices.InstanceId);
        Assert.Equal(["VMC5AX"], devices.DeviceNames);
        Assert.Equal(27, devices.DataItems.Count);
        Assert.Equal(new DataItemInfo("xpos", "SAMPLE", "POSITION", "ACTUAL", "MILLIMETER"), devices.DataItems["xpos"]);
        Assert.Equal(new DataItemInfo("srpm", "SAMPLE", "ROTARY_VELOCITY", "ACTUAL", "REVOLUTION/MINUTE"), devices.DataItems["srpm"]);
        Assert.Equal(new DataItemInfo("system", "CONDITION", "SYSTEM", null, null), devices.DataItems["system"]);
    }

    [Fact]
    public void ParseStreams_SimulatorCurrent_ReturnsHeaderAndObservationsSortedBySequence()
    {
        var streams = MTConnectParser.ParseStreams(Fixtures.Read("current.xml"));

        Assert.Equal(new StreamsHeader(Fixtures.InstanceId, 388, 1, 387, 131072), streams.Header);
        Assert.Equal(27, streams.Observations.Count);
        Assert.Equal(streams.Observations.Select(o => o.Sequence).Order(), streams.Observations.Select(o => o.Sequence));
        Assert.Equal(
            new MTConnectObservation("VMC5AX", Fixtures.InstanceId, "xpos", 370,
                new DateTimeOffset(2026, 11, 4, 11, 43, 7, 450, TimeSpan.Zero),
                ObservationCategory.Sample, "POSITION", "ACTUAL", null, "103.5627", null, null),
            Assert.Single(streams.Observations, o => o.DataItemId == "xpos"));
    }

    [Fact]
    public void ParseStreams_SimulatorSample_ReturnsConsecutiveSequencesAndCatchUpHeader()
    {
        var streams = MTConnectParser.ParseStreams(Fixtures.Read("sample.xml"));

        Assert.Equal(new StreamsHeader(Fixtures.InstanceId, 41, 1, 427, 131072), streams.Header);
        Assert.Equal(Enumerable.Range(1, 40).Select(i => (long)i), streams.Observations.Select(o => o.Sequence));
        Assert.All(streams.Observations, o => Assert.Equal(TimeSpan.Zero, o.Timestamp.Offset));
    }

    [Fact]
    public void ParseStreams_SimulatorSample_MapsCategoryTypeAndValueFromElements()
    {
        var observations = MTConnectParser.ParseStreams(Fixtures.Read("sample.xml")).Observations;

        var avail = observations.Single(o => o.Sequence == 1);
        Assert.Equal(("avail", ObservationCategory.Event, "AVAILABILITY", "AVAILABLE"), (avail.DataItemId, avail.Category, avail.Type, avail.Value));
        var feed = observations.Single(o => o.Sequence == 8);
        Assert.Equal(("feed", ObservationCategory.Sample, "PATH_FEEDRATE", "ACTUAL", "42.868"), (feed.DataItemId, feed.Category, feed.Type, feed.SubType, feed.Value));
        var system = observations.Single(o => o.Sequence == 9);
        Assert.Equal((ObservationCategory.Condition, "SYSTEM", "", ConditionLevel.Normal), (system.Category, system.Type, system.Value, system.ConditionLevel));
    }

    [Fact]
    public void ParseStreams_ConditionsAndUnavailable_MapsLevelNativeCodeAndRawValue()
    {
        var observations = MTConnectParser.ParseStreams(Fixtures.Read("sample-conditions.xml")).Observations;

        Assert.Equal([5000L, 5001L, 5002L, 5003L], observations.Select(o => o.Sequence));
        var normal = observations[0];
        Assert.Equal((ConditionLevel.Normal, "", (string?)null), (normal.ConditionLevel, normal.Value, normal.NativeCode));
        Assert.Equal(("srpm", "ROTARY_VELOCITY", "UNAVAILABLE"), (observations[2].DataItemId, observations[2].Type, observations[2].Value));
        var fault = observations[3];
        Assert.Equal(
            ("system", ObservationCategory.Condition, "SYSTEM", ConditionLevel.Fault, "SPINDLE OVERLOAD", "AL-0123"),
            (fault.DataItemId, fault.Category, fault.Type, fault.ConditionLevel, fault.Value, fault.NativeCode));
    }

    [Fact]
    public void ParseStreams_EmptySample_ReturnsHeaderAndNoObservations()
    {
        var streams = MTConnectParser.ParseStreams(Fixtures.Read("sample-empty.xml"));

        Assert.Equal(new StreamsHeader(Fixtures.InstanceId, 1568, 1, 1567, 131072), streams.Header);
        Assert.Empty(streams.Observations);
    }

    [Theory]
    [InlineData("streams-foreign-namespace.xml")]
    [InlineData("malformed.xml")]
    [InlineData("probe.xml")]
    [InlineData("error-out-of-range.xml")]
    public void ParseStreams_UnsupportedDocument_ThrowsFormatException(string fixture)
    {
        Assert.Throws<MTConnectFormatException>(() => MTConnectParser.ParseStreams(Fixtures.Read(fixture)));
    }

    [Fact]
    public void ParseStreams_MissingHeaderAttribute_ThrowsFormatException()
    {
        var xml = Fixtures.Read("sample-empty.xml").Replace("nextSequence=\"1568\" ", "");

        Assert.Throws<MTConnectFormatException>(() => MTConnectParser.ParseStreams(xml));
    }

    [Fact]
    public void ParseDevices_StreamsDocument_ThrowsFormatException()
    {
        Assert.Throws<MTConnectFormatException>(() => MTConnectParser.ParseDevices(Fixtures.Read("current.xml")));
    }

    [Fact]
    public void TryParseError_OutOfRange_ReturnsErrorCode()
    {
        var error = MTConnectParser.TryParseError(Fixtures.Read("error-out-of-range.xml"));

        Assert.NotNull(error);
        Assert.Equal(MTConnectAgentErrorException.OutOfRange, error.ErrorCode);
    }

    [Theory]
    [InlineData("current.xml")]
    [InlineData("malformed.xml")]
    public void TryParseError_NotAnErrorDocument_ReturnsNull(string fixture)
    {
        Assert.Null(MTConnectParser.TryParseError(Fixtures.Read(fixture)));
    }
}
