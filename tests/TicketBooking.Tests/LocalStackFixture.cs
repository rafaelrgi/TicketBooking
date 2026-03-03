using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Testcontainers.LocalStack;
using Xunit;

public class LocalStackFixture : IAsyncLifetime
{
    private readonly LocalStackContainer _localStackContainer =
        new LocalStackBuilder("localstack/localstack:latest").Build();

    public string StateMachineArn { get; private set; } = null!;
    public IAmazonDynamoDB DynamoDb { get; private set; } = null!;
    public IAmazonStepFunctions StepFunctions { get; private set; } = null!;
    public IAmazonSQS Sqs { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _localStackContainer.StartAsync();

        var config = new AmazonDynamoDBConfig { ServiceURL = _localStackContainer.GetConnectionString() };
        var sqsConfig = new AmazonSQSConfig { ServiceURL = _localStackContainer.GetConnectionString() };
        var sfConfig = new AmazonStepFunctionsConfig { ServiceURL = _localStackContainer.GetConnectionString() };

        DynamoDb = new AmazonDynamoDBClient(new AnonymousAWSCredentials(), config);
        Sqs = new AmazonSQSClient(new AnonymousAWSCredentials(), sqsConfig);
        StepFunctions = new AmazonStepFunctionsClient(new AnonymousAWSCredentials(), sfConfig);

        await CreateResources(DynamoDb, StepFunctions, Sqs);
    }

    private async Task CreateResources(IAmazonDynamoDB dynamoDb, IAmazonStepFunctions stepFunctions, IAmazonSQS sqs)
    {
        // Create Tickets table
        await dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = "Tickets",
            AttributeDefinitions = [new("PK", ScalarAttributeType.S), new("SK", ScalarAttributeType.S)],
            KeySchema = [new("PK", KeyType.HASH), new("SK", KeyType.RANGE)],
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        });

        // Create SQS queue 
        var queueResponse = await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = "TicketUpdatesQueue",
            Attributes = new Dictionary<string, string>
            {
                { "VisibilityTimeout", "2" }
            }
        });
        var queueUrl = queueResponse.QueueUrl;

        // Create State Machine (Step functions)
        var definition = $@"
{{
  ""StartAt"": ""WaitShort"",
  ""States"": {{
    ""WaitShort"": {{ ""Type"": ""Wait"", ""Seconds"": 1, ""Next"": ""CancelReservation"" }},
    ""CancelReservation"": {{
      ""Type"": ""Task"",
      ""Resource"": ""arn:aws:states:::dynamodb:updateItem"",
      ""ResultPath"": ""$.updateResult"",
      ""Parameters"": {{
        ""TableName"": ""Tickets"",
        ""Key"": {{ ""PK"": {{ ""S.$"": ""$.PK"" }}, ""SK"": {{ ""S.$"": ""$.SK"" }} }},
        ""UpdateExpression"": ""SET #s = :cancelled"",
        ""ExpressionAttributeNames"": {{ ""#s"": ""Status"" }},
        ""ExpressionAttributeValues"": {{ "":cancelled"": {{ ""S"": ""Cancelled"" }} }}
      }},
      ""Next"": ""NotifyQueue""
    }},
    ""NotifyQueue"": {{
      ""Type"": ""Task"",
      ""Resource"": ""arn:aws:states:::sqs:sendMessage"",
      ""Parameters"": {{
        ""QueueUrl"": ""{queueUrl}"",
        ""MessageBody"": {{ ""PK.$"": ""$.PK"", ""SK.$"": ""$.SK"", ""Status"": ""Cancelled"" }}
      }},
      ""End"": true
    }}
  }}
}}";

        var createResponse = await stepFunctions.CreateStateMachineAsync(new CreateStateMachineRequest
        {
            Name = "TicketBookingWorkflowTest",
            Definition = definition,
            RoleArn = "arn:aws:iam::000000000000:role/stepfunctions-role"
        });

        StateMachineArn = createResponse.StateMachineArn;
    }

    public async ValueTask DisposeAsync()
    {
        await _localStackContainer.DisposeAsync();
    }
}
