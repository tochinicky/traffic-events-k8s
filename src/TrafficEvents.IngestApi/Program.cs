using TrafficEvents.Infrastructure.Hosting;
using TrafficEvents.Infrastructure.Messaging;
using TrafficEvents.IngestApi;

const string ServiceName = "ingest-api";

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(ServiceName);
builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection(IngestOptions.SectionName));
builder.Services.AddRabbitMq(builder.Configuration);
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();

var app = builder.Build();
app.MapServiceDefaults(ServiceName);
app.MapPost("/events", IngestEndpoint.HandleAsync)
    .AddEndpointFilter<ApiKeyFilter>()
    .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1024 * 1024));

app.Run();
