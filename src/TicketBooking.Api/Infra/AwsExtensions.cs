using Amazon.DynamoDBv2;
using Amazon.SQS;
using Amazon.StepFunctions;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Domain.Settings;
using TicketBooking.Infra.Adapters;

namespace TicketBooking.Api.Infra;

public static class AwsExtensions
{
    public static IServiceCollection AddAws(this IServiceCollection services, IConfiguration config,
        IWebHostEnvironment environment)
    {
        var settingsAws = config.GetSection(SettingsAws.SectionName).Get<SettingsAws>();
        if (settingsAws == null)
            throw new ArgumentNullException(nameof(services));

        var awsOptions = config.GetAWSOptions();
        if (!string.IsNullOrEmpty(settingsAws.ServiceUrl))
        {
            awsOptions.DefaultClientConfig.ServiceURL = settingsAws.ServiceUrl;
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
                    ServiceURL = settingsAws.ServiceUrl,
                    AuthenticationRegion = settingsAws.Region
                };
                return new AmazonDynamoDBClient(credentials, cfg);
            });
            services.AddSingleton<IAmazonSQS>(sp =>
            {
                var cfg = new AmazonSQSConfig
                {
                    ServiceURL = settingsAws.ServiceUrl,
                    AuthenticationRegion = settingsAws.Region
                };
                return new AmazonSQSClient(credentials, cfg);
            });

            services.AddSingleton<IAmazonStepFunctions>(sp =>
            {
                var cfg = new AmazonStepFunctionsConfig
                {
                    ServiceURL = settingsAws.ServiceUrl,
                    AuthenticationRegion = settingsAws.Region
                };
                return new AmazonStepFunctionsClient(credentials, cfg);
            });
        }
        //! IsDevelopment
        else
        {
            services.AddAWSService<IAmazonDynamoDB>();
            services.AddAWSService<IAmazonSQS>();
            services.AddAWSService<IAmazonStepFunctions>();
        }

        services.AddDefaultAWSOptions(awsOptions);

        var queueUrl = settingsAws.TicketUpdatesQueue  ?? "";
        services.AddSingleton<IServiceBus>(sp => new SqsServiceBus(sp.GetRequiredService<IAmazonSQS>(), queueUrl));

        return services;
    }
}
