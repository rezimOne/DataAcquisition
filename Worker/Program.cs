using DataAcquisition.Worker;
using DataAcquisition.Worker.Api;
using DataAcquisition.Worker.MTConnect;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMTConnect();
builder.Services.AddDataAcquisitionApi();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
