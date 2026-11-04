using DataAcquisition.Api.ApiKey;
using DataAcquisition.Api.Observations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddApiKeyAuthentication();
builder.Services.AddSingleton<IObservationStore, InMemoryObservationStore>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapObservationEndpoints();

app.Run();
