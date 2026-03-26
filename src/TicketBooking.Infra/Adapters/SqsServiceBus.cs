using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.SQS;
using Amazon.SQS.Model;
using TicketBooking.Domain.Common;
using TicketBooking.Domain.Interfaces;

namespace TicketBooking.Infra.Adapters;

public class SqsServiceBus : IServiceBus
{
    private readonly IAmazonSQS _sqsClient;
    public string QueueUrl { get; }


    public SqsServiceBus(IAmazonSQS sqsClient, string queueUrl)
    {
        _sqsClient = sqsClient;
        QueueUrl = queueUrl;
    }

    public async Task Publish<T>(T message, CancellationToken ct = default) where T : class
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(message, options);

        var request = new SendMessageRequest
        {
            QueueUrl = QueueUrl,
            MessageBody = json,
        };
        await _sqsClient.SendMessageAsync(request, ct);
    }

    public async Task Subscribe<T>(Func<T, CancellationToken, Task<bool>> handler, CancellationToken cancelToken) where T : class
    {
        while (!cancelToken.IsCancellationRequested)
        {
            var request = new ReceiveMessageRequest { QueueUrl = QueueUrl, WaitTimeSeconds = 20 };
            var response = await _sqsClient.ReceiveMessageAsync(request, cancelToken);

            if (response?.Messages is not { Count: > 0 })
                continue;

            foreach (var message in response.Messages)
            {
                var dto = JsonSerializer.Deserialize<T>(message.Body, JsonDefaults.Options);
                if (dto == null)
                    continue;

                if (await handler(dto, cancelToken))
                {
                    await _sqsClient.DeleteMessageAsync(QueueUrl, message.ReceiptHandle, cancelToken);
                }
            }
        }
    }
}
