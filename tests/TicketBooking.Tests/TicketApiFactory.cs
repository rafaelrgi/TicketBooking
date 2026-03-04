using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.LocalStack;
using Microsoft.AspNetCore.SignalR.Client;

namespace TicketBooking.Tests.Integration;

public class TicketApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly LocalStackContainer _localStack = new LocalStackBuilder("localstack/localstack:latest").Build();

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
        });
    }

    public async ValueTask InitializeAsync()
    {
        await _localStack.StartAsync();

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
    }

    public HubConnection CreateHubConnection()
    {
        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/ticketHub", o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
            })
            .Build();
        return hubConnection;
    }

    public new async Task DisposeAsync() => await _localStack.DisposeAsync();
}
