using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleEmail;

namespace MovieNight.Library.Builders;

internal static class ServiceProviderBuilder
{
    public static IServiceProvider Build() =>
        new ServiceCollection()
            .AddUtilities()
            .AddVendorServices()
            .AddBusinessServices()
            .AddValidators()
            .BuildServiceProvider();

    private static IServiceCollection AddVendorServices(this IServiceCollection services)
    {
        var region = RegionEndpoint.GetBySystemName(EnvReader.GetStringValue("AWS_REGION"));
        return services
            .AddDefaultAWSOptions(new AWSOptions { Region = region })
            .AddAWSService<IAmazonDynamoDB>()
            .AddSingleton<IAmazonSimpleEmailService, AmazonSimpleEmailServiceClient>(); // AddAWSService fails for SES
    }

    internal static IServiceCollection AddUtilities(this IServiceCollection services)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        return services
            .AddLogging(builder => builder.AddLambdaLogger())
            .AddSingleton(options)
            .AddSingleton<ApiGatewayAdapter>();
    }

    internal static IServiceCollection AddValidators(this IServiceCollection services) =>
        services
            .AddSingleton<IValidator<CreateNightRequest>, CreateNightRequestValidator>()
            .AddSingleton<IValidator<SubmitSuggestionRequest>, SubmitSuggestionRequestValidator>();

    /// <summary>
    /// Keys are the HTTP API route key, lower-cased: "post /api/nights". They have to match the
    /// routes in iac/terraform/api-gateway.tf, which is the other place this list is written down.
    /// </summary>
    internal static IServiceCollection AddBusinessServices(this IServiceCollection services) =>
        services
            .AddSingleton<NightProvider>()
            .AddSingleton<EmailSender>()
            .AddSingleton<NightReader>()
            .AddSingleton<PhaseAdvancer>()

            .AddKeyedSingleton<IApiGatewayHandler, HealthService>("get /api/health")
            .AddKeyedSingleton<IApiGatewayHandler, CreateNightService>("post /api/nights")
            .AddKeyedSingleton<IApiGatewayHandler, GetNightService>("get /api/nights/{nightid}")
            .AddKeyedSingleton<IApiGatewayHandler, SubmitSuggestionService>("post /api/nights/{nightid}/suggestion")
            .AddKeyedSingleton<IApiGatewayHandler, SubmitRankingService>("post /api/nights/{nightid}/ranking")
            .AddKeyedSingleton<IApiGatewayHandler, AdvanceNightService>("post /api/nights/{nightid}/advance");
}
