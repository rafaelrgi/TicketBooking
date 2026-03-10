using TicketBooking.Api;
using TicketBooking.Api.Endpoints;
using TicketBooking.Api.Hubs;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Infra.Repositories;
using TicketBooking.Application.Interfaces;
using TicketBooking.Infra.Caching;
using TicketBooking.Api.Infra;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuth(builder.Configuration);

// Redis setup
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
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
        policy.WithOrigins("http://localhost:5025", "http://127.0.0.1:5025")
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
app.MapHub<TicketHub>("/tickethub");
app.MapEventEndpoints();
app.MapTicketEndpoints();

app.Run();

public partial class Program
{
}
