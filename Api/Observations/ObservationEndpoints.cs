using DataAcquisition.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DataAcquisition.Api.Observations;

public static class ObservationEndpoints
{
    public static IEndpointRouteBuilder MapObservationEndpoints(this IEndpointRouteBuilder app)
    {
        var observations = app.MapGroup(ApiContract.ObservationsPath).RequireAuthorization();
        observations.MapPost("", AddObservationsAsync);
        observations.MapGet("", QueryObservationsAsync);
        return app;
    }

    private static async Task<Results<Ok<ObservationBatchResponse>, ValidationProblem>> AddObservationsAsync(
        ObservationBatchRequest request,
        IObservationStore store,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var errors = ObservationBatchValidator.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var deviceId = request.DeviceId!;
        var instanceId = request.InstanceId!.Value;
        var observations = request.Observations!
            .Select(o => new StoredObservationDto(
                deviceId,
                instanceId,
                o!.DataItemId!,
                o.Sequence!.Value,
                o.Timestamp!.Value.ToUniversalTime(),
                o.Category!.Value,
                o.Type,
                o.SubType,
                o.Value!,
                o.ConditionLevel))
            .ToList();

        var result = await store.AddAsync(observations, cancellationToken);

        var logger = loggerFactory.CreateLogger(typeof(ObservationEndpoints));
        logger.LogInformation(
            "Observation batch from {DeviceId} (instance {InstanceId}): {Accepted} accepted, {Duplicates} duplicates.",
            deviceId, instanceId, result.Accepted, result.Duplicates);

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ObservationQueryResponse>, ValidationProblem>> QueryObservationsAsync(
        string? deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        IObservationStore store,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            errors["deviceId"] = ["deviceId is required."];
        }
        if (from is not null && to is not null && from >= to)
        {
            errors["from"] = ["from must be earlier than to."];
        }
        var effectiveLimit = limit ?? ApiContract.DefaultQueryLimit;
        if (effectiveLimit is < 1 or > ApiContract.MaxQueryLimit)
        {
            errors["limit"] = [$"limit must be between 1 and {ApiContract.MaxQueryLimit}."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await store.QueryAsync(new ObservationQuery(deviceId!, from, to, effectiveLimit), cancellationToken);
        return TypedResults.Ok(result);
    }
}
