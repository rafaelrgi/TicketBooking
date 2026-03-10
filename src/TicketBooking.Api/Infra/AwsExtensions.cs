using Amazon.DynamoDBv2;
using Amazon.SQS;
using Amazon.StepFunctions;

namespace TicketBooking.Api.Infra;

public static class AwsExtensions
{
    public static IServiceCollection AddAws(this IServiceCollection services, IConfiguration config,  IWebHostEnvironment environment)
    {
        var awsOptions = config.GetAWSOptions();
        var customServiceUrl = Environment.GetEnvironmentVariable("AWS_SERVICE_URL");
        if (!string.IsNullOrEmpty(customServiceUrl))
        {
            awsOptions.DefaultClientConfig.ServiceURL = customServiceUrl;
            awsOptions.DefaultClientConfig.UseHttp = true;
        }

        if (environment.IsDevelopment())
        {
            var credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test");
            awsOptions.Credentials = credentials;
            services.AddSingleton<IAmazonDynamoDB>(sp =>
            {
                var cfg = new AmazonDynamoDBConfig
                {
                    ServiceURL = "http://localhost:4566",
                    AuthenticationRegion = "sa-east-1"
                };
                return new AmazonDynamoDBClient(credentials, cfg);
            });
            services.AddSingleton<IAmazonSQS>(sp =>
            {
                var cfg = new AmazonSQSConfig
                {
                    ServiceURL = "http://localhost:4566",
                    AuthenticationRegion = "sa-east-1"
                };
                return new AmazonSQSClient(credentials, cfg);
            });

            services.AddSingleton<IAmazonStepFunctions>(sp =>
            {
                var cfg = new AmazonStepFunctionsConfig
                {
                    ServiceURL = "http://localhost:4566",
                    AuthenticationRegion = "sa-east-1"
                };
                return new AmazonStepFunctionsClient(credentials, cfg);
            });
        }
        else
        {
            services.AddAWSService<IAmazonDynamoDB>();
            services.AddAWSService<IAmazonStepFunctions>();
        }

        services.AddDefaultAWSOptions(awsOptions);

        return services;
    }
}
