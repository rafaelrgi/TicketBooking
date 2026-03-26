using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Context.Propagation;
using TicketBooking.Api.Endpoints;
using TicketBooking.Api.Hubs;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Infra.Repositories;
using TicketBooking.Application.Interfaces;
using TicketBooking.Infra.Caching;
using TicketBooking.Api.Infra;
using TicketBooking.Domain.Settings;
using TicketBooking.Infra.Settings;
using Serilog;
using Serilog.Enrichers.Span;
using TicketBooking.Api.Workers;
using TicketBooking.Domain.Common;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("TicketApi"))
            .SetSampler(new AlwaysOnSampler())
            .AddAspNetCoreInstrumentation()
            .AddSource("TicketBooking.Telemetry")
            .AddOtlpExporter(opt => opt.Endpoint = new Uri("http://localhost:4317")))
        .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("TicketBooking.Metrics")
                .AddPrometheusExporter()
            //.AddOtlpExporter() //Aspire Dashboard
            //.AddOtlpExporter(opt => opt.Endpoint = new Uri("http://localhost:4317")) //Jaeger Dashboard
        );

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithSpan()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = "http://localhost:4317";
            //options.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc; //Jaeger
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = "TicketBooking-API"
            };
        })
    );

    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .CreateBootstrapLogger();
    Log.Information("Starting Api...");

    builder.Services.AddSettings(builder.Configuration);
    var settingsUrls = builder.Configuration.GetSection(SettingsUrls.SectionName).Get<SettingsUrls>()!;

    builder.Services.AddAuth(builder.Configuration);

// Redis setup
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? settingsUrls.Redis;
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "TicketBooking_";
    });

    builder.Services.AddAws(builder.Configuration, builder.Environment);

    builder.Services.AddHostedService<TicketUpdateWorker>();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddSingleton<ITicketCacheService, TicketCacheService>();
    builder.Services.AddScoped<IEventRepository, DynamoDbEventRepository>();
    builder.Services.AddScoped<ITicketRepository, DynamoDbTicketRepository>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddSignalR();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (settingsUrls.AllowedOrigins != null)
                policy.WithOrigins(settingsUrls.AllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
        });
    });

    Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

    builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(opt =>
    {
        opt.SerializerOptions.Converters.Clear();
        foreach (var converter in JsonDefaults.Options.Converters)
            opt.SerializerOptions.Converters.Add(converter);
        opt.SerializerOptions.PropertyNameCaseInsensitive = JsonDefaults.Options.PropertyNameCaseInsensitive;
    });

    var app = builder.Build();

// Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
        app.UseHttpsRedirection();

    app.UseRouting();
    app.UseCors();
// auth
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseWebSockets();
    app.MapHub<TicketHub>(settingsUrls.TicketHub);

    app.MapEventEndpoints();
    app.MapTicketEndpoints();

    app.UseOpenTelemetryPrometheusScrapingEndpoint();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Something went terribly wrong starting Api!");
}
finally
{
    Log.CloseAndFlush();
}

// Needed for tests
public partial class Program
{
}
