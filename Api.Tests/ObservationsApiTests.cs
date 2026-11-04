using System.Net;
using System.Net.Http.Json;
using System.Text;
using DataAcquisition.Contracts;
using Microsoft.AspNetCore.Http;
using Xunit;
using static DataAcquisition.Api.Tests.TestData;

namespace DataAcquisition.Api.Tests;

public sealed class ObservationsApiTests : IAsyncDisposable
{
    private const string ProblemJson = "application/problem+json";

    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ObservationsApiTests() => _client = _factory.CreateAuthorizedClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // AC-02

    [Fact]
    public async Task Post_ValidBatchWithSampleAndCondition_AcceptsAndReturnsFromQuery()
    {
        var sample = Sample(1201) with { Timestamp = new DateTimeOffset(2026, 11, 4, 12, 15, 30, 120, TimeSpan.FromHours(2)) };
        var condition = Condition(1202);

        var post = await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(sample, condition), Ct);

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal(new ObservationBatchResponse(2, 0), await post.Content.ReadFromJsonAsync<ObservationBatchResponse>(Ct));

        var items = (await QueryAsync($"?deviceId={DeviceId}")).Items;
        Assert.Equal(2, items.Count);
        Assert.Equal(
            new StoredObservationDto(DeviceId, InstanceId, "xpos", 1201, new DateTimeOffset(2026, 11, 4, 10, 15, 30, 120, TimeSpan.Zero),
                ObservationCategory.Sample, "POSITION", "ACTUAL", "112.457", null),
            items[0]);
        Assert.Equal(TimeSpan.Zero, items[0].Timestamp.Offset);
        Assert.Equal(
            new StoredObservationDto(DeviceId, InstanceId, "system", 1202, condition.Timestamp!.Value,
                ObservationCategory.Condition, "SYSTEM", null, "SPINDLE OVERLOAD", ConditionLevel.Fault),
            items[1]);
    }

    [Fact]
    public async Task Post_ValidBatch_UsesCamelCaseAndStringEnumsOnWire()
    {
        await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Condition(1)), Ct);

        var json = await _client.GetStringAsync($"{ApiContract.ObservationsPath}?deviceId={DeviceId}", Ct);

        Assert.Contains("\"category\":\"Condition\"", json);
        Assert.Contains("\"conditionLevel\":\"Fault\"", json);
        Assert.Contains("\"truncated\":false", json);
    }

    // AC-03

    [Fact]
    public async Task Post_WithoutApiKey_Returns401AndStoresNothing()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Sample(1)), Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Empty((await QueryAsync($"?deviceId={DeviceId}")).Items);
    }

    [Fact]
    public async Task Post_WithInvalidApiKey_Returns401AndStoresNothing()
    {
        using var wrongKey = _factory.CreateClient();
        wrongKey.DefaultRequestHeaders.Add(ApiContract.ApiKeyHeader, "wrong-key");

        var response = await wrongKey.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Sample(1)), Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Empty((await QueryAsync($"?deviceId={DeviceId}")).Items);
    }

    [Fact]
    public async Task Post_WithoutApiKeyAndInvalidBody_Returns401NotValidationError()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsync(ApiContract.ObservationsPath, JsonContent("{ not json"), Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_WithoutApiKey_Returns401()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync($"{ApiContract.ObservationsPath}?deviceId={DeviceId}", Ct);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    // AC-04

    public static TheoryData<string, string> InvalidBatches => new()
    {
        { "empty list", """{"deviceId":"VMC5AX","instanceId":1,"observations":[]}""" },
        { "missing deviceId", """{"instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Sample","value":"1"}]}""" },
        { "empty dataItemId", """{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Sample","value":"1"}]}""" },
        { "sequence 0", """{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":0,"timestamp":"2026-11-04T10:15:30Z","category":"Sample","value":"1"}]}""" },
        { "condition without level", """{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"system","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Condition","value":"x"}]}""" },
        { "sample with level", """{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Sample","value":"1","conditionLevel":"Normal"}]}""" },
        { "duplicate sequence", """{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Sample","value":"1"},{"dataItemId":"ypos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Sample","value":"2"}]}""" },
        { "one valid one invalid", """{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Sample","value":"1"},{"dataItemId":"ypos","sequence":2,"timestamp":"2026-11-04T10:15:30Z","category":"Sample"}]}""" },
    };

    [Theory]
    [MemberData(nameof(InvalidBatches))]
    public async Task Post_InvalidBatch_Returns400WithFieldErrorsAndStoresNothing(string caseName, string body)
    {
        var response = await _client.PostAsync(ApiContract.ObservationsPath, JsonContent(body), Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        Assert.NotEmpty(problem!.Errors);
        Assert.Empty((await QueryAsync($"?deviceId={DeviceId}")).Items);
        Assert.NotNull(caseName);
    }

    [Fact]
    public async Task Post_MoreThanMaxBatchSize_Returns400AndStoresNothing()
    {
        var observations = Enumerable.Range(1, ApiContract.MaxBatchSize + 1).Select(i => Sample(i)).ToArray();

        var response = await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(observations), Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Empty((await QueryAsync($"?deviceId={DeviceId}")).Items);
    }

    [Fact]
    public async Task Post_InvalidObservationField_ErrorKeyPointsToField()
    {
        var response = await _client.PostAsJsonAsync(
            ApiContract.ObservationsPath, Batch(Sample(1), Sample(2), Sample(3), Sample(4) with { DataItemId = "" }), Ct);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        Assert.Contains("observations[3].dataItemId", problem!.Errors.Keys);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Bogus","value":"1"}]}""")]
    [InlineData("""{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"not-a-date","category":"Sample","value":"1"}]}""")]
    public async Task Post_MalformedJson_Returns400ProblemDetailsAndStoresNothing(string body)
    {
        var response = await _client.PostAsync(ApiContract.ObservationsPath, JsonContent(body), Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Empty((await QueryAsync($"?deviceId={DeviceId}")).Items);
    }

    [Theory]
    [InlineData("""{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":7,"value":"1"}]}""")]
    [InlineData("""{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":0,"value":"1"}]}""")]
    [InlineData("""{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"7","value":"1"}]}""")]
    [InlineData("""{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"system","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Condition","value":"x","conditionLevel":42}]}""")]
    [InlineData("""{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"system","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Condition","value":"x","conditionLevel":1}]}""")]
    public async Task Post_NumericEnumValue_Returns400AndStoresNothing(string body)
    {
        var response = await _client.PostAsync(ApiContract.ObservationsPath, JsonContent(body), Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Empty((await QueryAsync($"?deviceId={DeviceId}")).Items);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData(null)]
    public async Task Post_NonJsonContentType_Returns415AndStoresNothing(string? mediaType)
    {
        using var content = new StringContent(
            """{"deviceId":"VMC5AX","instanceId":1,"observations":[{"dataItemId":"xpos","sequence":1,"timestamp":"2026-11-04T10:15:30Z","category":"Sample","value":"1"}]}""");
        content.Headers.ContentType = mediaType is null ? null : new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);

        var response = await _client.PostAsync(ApiContract.ObservationsPath, content, Ct);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
        Assert.Empty((await QueryAsync($"?deviceId={DeviceId}")).Items);
    }

    // AC-05

    [Fact]
    public async Task Post_SameBatchTwice_SecondReportsDuplicatesOnly()
    {
        var batch = Batch(Sample(1), Sample(2), Condition(3));
        await _client.PostAsJsonAsync(ApiContract.ObservationsPath, batch, Ct);

        var second = await _client.PostAsJsonAsync(ApiContract.ObservationsPath, batch, Ct);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(new ObservationBatchResponse(0, 3), await second.Content.ReadFromJsonAsync<ObservationBatchResponse>(Ct));
        Assert.Equal(3, (await QueryAsync($"?deviceId={DeviceId}")).Items.Count);
    }

    [Fact]
    public async Task Post_OverlappingBatch_AcceptsOnlyNewObservations()
    {
        await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Sample(1), Sample(2)), Ct);

        var response = await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Sample(1), Sample(2), Sample(3)), Ct);

        Assert.Equal(new ObservationBatchResponse(1, 2), await response.Content.ReadFromJsonAsync<ObservationBatchResponse>(Ct));
        Assert.Equal([1L, 2L, 3L], (await QueryAsync($"?deviceId={DeviceId}")).Items.Select(o => o.Sequence));
    }

    [Fact]
    public async Task Post_ParallelIdenticalBatches_StoresEachObservationOnce()
    {
        var batch = Batch(Enumerable.Range(1, 200).Select(i => Sample(i)).ToArray());

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => _client.PostAsJsonAsync(ApiContract.ObservationsPath, batch, Ct)));
        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<ObservationBatchResponse>(Ct)));

        Assert.Equal(200, results.Sum(r => r!.Accepted));
        Assert.Equal(3 * 200, results.Sum(r => r!.Duplicates));
        Assert.Equal(200, (await QueryAsync($"?deviceId={DeviceId}&limit=1000")).Items.Count);
    }

    // AC-06

    [Fact]
    public async Task Get_FilterByDeviceTimeRangeAndLimit_ReturnsSortedPageWithTruncatedFlag()
    {
        await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(DeviceId, InstanceId + 1, Sample(1), Sample(2)), Ct);
        await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(DeviceId, InstanceId, Sample(3), Sample(1), Sample(2), Sample(5)), Ct);
        await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch("OTHER", InstanceId, Sample(2), Sample(3)), Ct);
        var from = Uri.EscapeDataString(Sample(2).Timestamp!.Value.ToString("O"));
        var to = Uri.EscapeDataString(Sample(5).Timestamp!.Value.ToString("O"));

        var page = await QueryAsync($"?deviceId={DeviceId}&from={from}&to={to}&limit=2");
        var all = await QueryAsync($"?deviceId={DeviceId}&from={from}&to={to}");

        Assert.Equal([(InstanceId, 2L), (InstanceId, 3L)], page.Items.Select(o => (o.InstanceId, o.Sequence)));
        Assert.True(page.Truncated);
        Assert.Equal(
            [(InstanceId, 2L), (InstanceId, 3L), (InstanceId + 1, 2L)],
            all.Items.Select(o => (o.InstanceId, o.Sequence)));
        Assert.False(all.Truncated);
    }

    [Theory]
    [InlineData("", "deviceId")]
    [InlineData("?deviceId=VMC5AX&from=2026-11-04T10:00:00Z&to=2026-11-04T10:00:00Z", "from")]
    [InlineData("?deviceId=VMC5AX&from=2026-11-04T11:00:00Z&to=2026-11-04T10:00:00Z", "from")]
    [InlineData("?deviceId=VMC5AX&limit=0", "limit")]
    [InlineData("?deviceId=VMC5AX&limit=1001", "limit")]
    public async Task Get_InvalidParameters_Returns400WithFieldError(string query, string field)
    {
        var response = await _client.GetAsync(ApiContract.ObservationsPath + query, Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Get_UnknownDevice_Returns200WithEmptyList()
    {
        await _client.PostAsJsonAsync(ApiContract.ObservationsPath, Batch(Sample(1)), Ct);

        var result = await QueryAsync("?deviceId=UNKNOWN");

        Assert.Empty(result.Items);
        Assert.False(result.Truncated);
    }

    private async Task<ObservationQueryResponse> QueryAsync(string query)
    {
        var response = await _client.GetAsync(ApiContract.ObservationsPath + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObservationQueryResponse>(Ct))!;
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains($"\"status\":{(int)expected}", body);
    }
}
