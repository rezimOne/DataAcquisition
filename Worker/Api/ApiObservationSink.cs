using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataAcquisition.Contracts;
using DataAcquisition.Worker.MTConnect;
using Microsoft.Extensions.Options;

namespace DataAcquisition.Worker.Api;

/// <summary>
/// Sends observation batches to the data acquisition API (POST /api/v1/observations).
/// Returning normally means "delivered" (or deliberately skipped on 400); throwing makes the synchronizer retry
/// the same batch with backoff. Logs never contain observation values or the API key.
/// </summary>
internal sealed class ApiObservationSink(
    IHttpClientFactory httpClientFactory,
    IOptions<ApiOptions> options,
    ILogger<ApiObservationSink> logger) : IObservationSink
{
    public const string HttpClientName = "DataAcquisitionApi";

    // Relative to BaseAddress (which ends with '/'), so a base path like http://host/da/ is kept.
    private static readonly string ObservationsPath = ApiContract.ObservationsPath.TrimStart('/');

    public async Task ReceiveAsync(ObservationBatch batch, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, ObservationsPath)
        {
            Content = JsonContent.Create(batch.ToRequest()),
        };
        request.Headers.Add(ApiContract.ApiKeyHeader, options.Value.ApiKey);

        using var response = await client.SendAsync(request, cancellationToken);
        var firstSequence = batch.Observations[0].Sequence;
        var lastSequence = batch.Observations[^1].Sequence;

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ObservationBatchResponse>(cancellationToken)
                ?? throw new HttpRequestException("API returned an empty response for an observation batch.");
            logger.LogInformation(
                "Sent observations {FirstSequence}-{LastSequence} of {DeviceName} (instance {InstanceId}, {Source}): {Accepted} accepted, {Duplicates} duplicates.",
                firstSequence, lastSequence, batch.DeviceName, batch.InstanceId, batch.Source, result.Accepted, result.Duplicates);
            return;
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.BadRequest:
                // Retrying an invalid batch would block the stream forever, so it is skipped (decision in DA-004 intent).
                var fields = await ReadInvalidFieldsAsync(response, cancellationToken);
                logger.LogError(
                    "API rejected observations {FirstSequence}-{LastSequence} of {DeviceName} (instance {InstanceId}) as invalid; batch skipped. Invalid fields: {InvalidFields}.",
                    firstSequence, lastSequence, batch.DeviceName, batch.InstanceId, fields);
                return;

            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                // A configuration error: keep retrying so the data waits in the agent buffer until the key is fixed.
                logger.LogError(
                    "API rejected the API key (HTTP {StatusCode}) for observations {FirstSequence}-{LastSequence} of {DeviceName}; check {ConfigurationKey}. The batch will be retried.",
                    (int)response.StatusCode, firstSequence, lastSequence, batch.DeviceName, $"{ApiOptions.SectionName}:{nameof(ApiOptions.ApiKey)}");
                throw new HttpRequestException(
                    $"API rejected the API key (HTTP {(int)response.StatusCode}).", null, response.StatusCode);

            default:
                throw new HttpRequestException(
                    $"API returned HTTP {(int)response.StatusCode} for an observation batch.", null, response.StatusCode);
        }
    }

    /// <summary>Field paths from ValidationProblemDetails.errors (names only, never values).</summary>
    private static async Task<string> ReadInvalidFieldsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object)
            {
                return string.Join(", ", errors.EnumerateObject().Select(e => e.Name));
            }
            return "(no field details)";
        }
        catch (JsonException)
        {
            return "(response is not ProblemDetails JSON)";
        }
    }
}
