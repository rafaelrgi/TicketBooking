using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.LocalStack;
using Testcontainers.Redis;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Domain.Settings;
using TicketBooking.Infra.Adapters;

namespace TicketBooking.Tests.Integration;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly LocalStackContainer _localStack = new LocalStackBuilder("localstack/localstack:latest").Build();

    private readonly RedisContainer _redisCache = new RedisBuilder("redis:alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        try
        {
            var awsConnectionString = _localStack.GetConnectionString();

            builder.UseContentRoot(AppContext.BaseDirectory);
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables();

                var ticketUpdatesQueue = $"{SettingsAws.SectionName}:{nameof(SettingsAws.TicketUpdatesQueue)}";
                var serviceUrl = $"{SettingsAws.SectionName}:{nameof(SettingsAws.ServiceUrl)}";
                var ticketWorkflowArn = $"{SettingsAws.SectionName}:{nameof(SettingsAws.TicketWorkflowArn)}";
                var region = $"{SettingsAws.SectionName}:{nameof(SettingsAws.Region)}";
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    //["SqsSettings:QueueUrl"] = awsConnectionString + "/000000000000/TicketUpdatesQueue",
                    [ticketWorkflowArn] = "arn:aws:states:sa-east-1:000000000000:stateMachine:TicketBookingWorkflow",
                    [region] = "sa-east-1",
                    [ticketUpdatesQueue] = $"{awsConnectionString}/000000000000/TicketUpdatesQueue",
                    [serviceUrl] = _localStack.GetConnectionString()
                });
            });
            builder.UseEnvironment("Testing");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAmazonDynamoDB>();
                services.RemoveAll<IAmazonSQS>();

                services.AddSingleton<IAmazonDynamoDB>(sp =>
                {
                    var config = new AmazonDynamoDBConfig { ServiceURL = awsConnectionString };
                    return new AmazonDynamoDBClient(new BasicAWSCredentials("test", "test"), config);
                });

                services.AddSingleton<IAmazonSQS>(sp =>
                {
                    var config = new AmazonSQSConfig { ServiceURL = awsConnectionString };
                    return new AmazonSQSClient(new BasicAWSCredentials("test", "test"), config);
                });
                var queueUrl = $"{awsConnectionString}/000000000000/TicketUpdatesQueue";
                services.AddSingleton<IServiceBus>(sp => new SqsServiceBus(sp.GetRequiredService<IAmazonSQS>(), queueUrl));

                var redisConnectionString = _redisCache.GetConnectionString();
                services.AddStackExchangeRedisCache(options => { options.Configuration = redisConnectionString; });

                services.RemoveAll<IAmazonStepFunctions>();
                services.AddSingleton<IAmazonStepFunctions>(sp =>
                {
                    var config = new AmazonStepFunctionsConfig
                    {
                        ServiceURL = awsConnectionString,
                        AuthenticationRegion = "sa-east-1"
                    };
                    return new AmazonStepFunctionsClient(new BasicAWSCredentials("test", "test"), config);
                });

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
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR ConfigureWebHost: {ex.Message}");
            throw;
        }
    }

    public async ValueTask InitializeAsync()
    {
        await _localStack.StartAsync();
        await _redisCache.StartAsync();
        var awsConnectionString = _localStack.GetConnectionString();

        var sqsClient = new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig { ServiceURL = awsConnectionString });
        await sqsClient.CreateQueueAsync("TicketUpdatesQueue");

        var client = new AmazonDynamoDBClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonDynamoDBConfig { ServiceURL = awsConnectionString });

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

        await SetupStepFunctionsWorkflow();
    }

    private async Task SetupStepFunctionsWorkflow()
    {
        var stepClient = new AmazonStepFunctionsClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonStepFunctionsConfig
            {
                ServiceURL = _localStack.GetConnectionString(),
                AuthenticationRegion = "sa-east-1"
            });

        var binDir = AppContext.BaseDirectory;
        var definitionPath = Path.GetFullPath(Path.Combine(binDir, "../../../../../infra/workflow-definition.json"));

        if (!File.Exists(definitionPath))
            throw new FileNotFoundException($"ASL Definition not found: {definitionPath}");

        var aslDefinition = await File.ReadAllTextAsync(definitionPath);

        try
        {
            var response = await stepClient.CreateStateMachineAsync(new CreateStateMachineRequest
            {
                Name = "TicketBookingWorkflow",
                Definition = aslDefinition,
                RoleArn = "arn:aws:iam::000000000000:role/stepfunctions-role",
                Type = StateMachineType.STANDARD
            });
        }
        catch (StateMachineAlreadyExistsException)
        {
        }
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
