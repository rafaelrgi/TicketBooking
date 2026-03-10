using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Testcontainers.LocalStack;
using Testcontainers.Redis;

namespace TicketBooking.Tests.Integration;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly LocalStackContainer _localStack = new LocalStackBuilder("localstack/localstack:latest").Build();

    private readonly RedisContainer _redisCache = new RedisBuilder("redis:alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAmazonDynamoDB>();
            services.RemoveAll<IAmazonSQS>();

            services.AddSingleton<IAmazonDynamoDB>(sp =>
                new AmazonDynamoDBClient(new BasicAWSCredentials("test", "test"),
                    new AmazonDynamoDBConfig { ServiceURL = _localStack.GetConnectionString() }));

            services.AddSingleton<IAmazonSQS>(sp =>
                new AmazonSQSClient(new BasicAWSCredentials("test", "test"),
                    new AmazonSQSConfig { ServiceURL = _localStack.GetConnectionString() }));

            var dynamicQueueUrl = _localStack.GetConnectionString() + "/000000000000/TicketUpdatesQueue";

            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SqsSettings:QueueUrl"] = dynamicQueueUrl
                });
            });

            var redisConnectionString = _redisCache.GetConnectionString();
            services.AddStackExchangeRedisCache(options => { options.Configuration = redisConnectionString; });

            // Remove the real auth config
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(AuthenticationOptions));
            if (descriptor != null) services.Remove(descriptor);
            // Add the mocked auth config
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "TestScheme";
                    options.DefaultChallengeScheme = "TestScheme";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    "TestScheme", options => { });
        });
    }

    public async ValueTask InitializeAsync()
    {
        await _localStack.StartAsync();
        await _redisCache.StartAsync();

        var sqsClient = new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig { ServiceURL = _localStack.GetConnectionString() });
        await sqsClient.CreateQueueAsync("TicketUpdatesQueue");

        var client = new AmazonDynamoDBClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonDynamoDBConfig { ServiceURL = _localStack.GetConnectionString() });

        await client.CreateTableAsync(new CreateTableRequest
        {
            TableName = "Tickets",
            KeySchema = new List<KeySchemaElement> { new("PK", KeyType.HASH), new("SK", KeyType.RANGE) },
            AttributeDefinitions = new List<AttributeDefinition>
            {
                new("PK", ScalarAttributeType.S),
                new("SK", ScalarAttributeType.S)
            },
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        });

        //Yes, Ticket depends upon Events sometimes
        await client.CreateTableAsync(new CreateTableRequest
        {
            TableName = "Events",
            AttributeDefinitions = [new("PK", ScalarAttributeType.S)],
            KeySchema = [new("PK", KeyType.HASH)],
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        });
    }

    public HubConnection CreateHubConnection()
    {
        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/ticketHub", o => { o.HttpMessageHandlerFactory = _ => Server.CreateHandler(); })
            .Build();
        return hubConnection;
    }

    public async Task ClearCache()
    {
        await _redisCache.ExecAsync(new[] { "redis-cli", "FLUSHALL" });
    }

    public new async Task DisposeAsync()
    {
        await _redisCache.StopAsync();
        await _redisCache.DisposeAsync();
        await _localStack.DisposeAsync();
    }
}
