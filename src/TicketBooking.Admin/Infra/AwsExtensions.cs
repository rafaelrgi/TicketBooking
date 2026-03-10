/*
using Amazon.DynamoDBv2;

using Amazon.Runtime;

namespace TicketBooking.Admin.Infra;

public static class AwsExtensions
{
    public static IServiceCollection AddAws(this IServiceCollection services, IConfiguration config,  IWebHostEnvironment environment)
    {
        var awsOptions = config.GetAWSOptions();
        if (environment.IsDevelopment())
        {
            awsOptions.Credentials = new BasicAWSCredentials("test", "test");
        }
        services.AddDefaultAWSOptions(awsOptions);
        services.AddAWSService<IAmazonDynamoDB>();
        return services;
    }
}
*/
