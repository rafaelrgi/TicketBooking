using Amazon.DynamoDBv2;
using Amazon.StepFunctions;
using TicketBooking.Api;
using TicketBooking.Api.Endpoints;
using TicketBooking.Api.Hubs;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Infra.Repositories;
using Amazon.SQS;
using TicketBooking.Application.Interfaces;
using TicketBooking.Infra.Caching;

var builder = WebApplication.CreateBuilder(args);

// Redis setup
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.InstanceName = "TicketBooking_";
});

//  Aws setup
var awsOptions = builder.Configuration.GetAWSOptions();
var customServiceUrl = Environment.GetEnvironmentVariable("AWS_SERVICE_URL");
if (!string.IsNullOrEmpty(customServiceUrl))
{
    awsOptions.DefaultClientConfig.ServiceURL = customServiceUrl;
    awsOptions.DefaultClientConfig.UseHttp = true;
}

if (builder.Environment.IsDevelopment())
{
    var credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test");
    awsOptions.Credentials = credentials;
    builder.Services.AddSingleton<IAmazonDynamoDB>(sp =>
    {
        var config = new AmazonDynamoDBConfig
        {
            ServiceURL = "http://localhost:4566",
            AuthenticationRegion = "sa-east-1"
        };
        return new AmazonDynamoDBClient(credentials, config);
    });
    builder.Services.AddSingleton<IAmazonSQS>(sp =>
    {
        var config = new AmazonSQSConfig
        {
            ServiceURL = "http://localhost:4566",
            AuthenticationRegion = "sa-east-1"
        };
        return new AmazonSQSClient(credentials, config);
    });
    
    builder.Services.AddSingleton<IAmazonStepFunctions>(sp => 
    {
        var config = new AmazonStepFunctionsConfig 
        { 
            ServiceURL = "http://localhost:4566", 
            AuthenticationRegion = "sa-east-1"
        };
        return new AmazonStepFunctionsClient(credentials, config);
    });
}
else
{
    builder.Services.AddAWSService<IAmazonDynamoDB>();
    builder.Services.AddAWSService<IAmazonStepFunctions>();
}

builder.Services.AddHostedService<TicketUpdateWorker>();

builder.Services.AddDefaultAWSOptions(awsOptions);
// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddSingleton<ITicketCacheService, TicketCacheService>();
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

app.UseWebSockets();
app.MapHub<TicketHub>("/tickethub");
app.MapTicketEndpoints();

app.Run();

public partial class Program { }
