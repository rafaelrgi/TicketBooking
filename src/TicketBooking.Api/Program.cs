using System.Text.Json;
using TicketBooking.Api;
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
using TicketBooking.Api.Workers;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Api...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSettings(builder.Configuration);
    var settingsUrls = builder.Configuration.GetSection(SettingsUrls.SectionName).Get<SettingsUrls>()!;

    builder.Services.AddAuth(builder.Configuration);

    var globalJsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };
    builder.Services.AddSingleton(globalJsonOptions);

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
            policy.WithOrigins(settingsUrls.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
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

public partial class Program
{
}
