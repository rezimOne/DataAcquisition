namespace DataAcquisition.Contracts;

/// <summary>Constants shared by the API and its clients.</summary>
public static class ApiContract
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ObservationsPath = "/api/v1/observations";
    public const int MaxBatchSize = 1000;
    public const int MaxQueryLimit = 1000;
    public const int DefaultQueryLimit = 100;
}
