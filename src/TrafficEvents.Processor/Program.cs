using TrafficEvents.Infrastructure.Hosting;
using TrafficEvents.Processor;

const string ServiceName = "processor";

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(ServiceName);
builder.Services.AddProcessor(builder.Configuration);
builder.Services.AddHostedService<ConsumerHostedService>();

var app = builder.Build();
app.MapServiceDefaults(ServiceName);
app.Run();
