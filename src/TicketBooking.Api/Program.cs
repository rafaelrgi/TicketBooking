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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSettings(builder.Configuration);
builder.Services.Configure<SettingsUrls>(builder.Configuration.GetSection(SettingsUrls.SectionName));
builder.Services.Configure<SettingsAws>(builder.Configuration.GetSection(SettingsAws.SectionName));
builder.Services.Configure<SettingsAuth>(builder.Configuration.GetSection(SettingsAuth.SectionName));
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

public partial class Program
{
}
