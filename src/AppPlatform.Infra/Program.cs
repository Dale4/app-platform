using Amazon.CDK;
using AppPlatform.Infra.Configuration;
using AppPlatform.Infra.Stacks;
using Environment = Amazon.CDK.Environment;

var app = new App();

var envName = (app.Node.TryGetContext("env")?.ToString() ?? "dev").ToLowerInvariant();
if (envName is not ("dev" or "test"))
{
    throw new ArgumentException("Context 'env' must be 'dev' or 'test'. Example: cdk deploy --all -c env=dev");
}

var awsEnv = new Environment
{
    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION") ?? "us-west-1",
};

Tags.Of(app).Add("Project", "app-platform");
Tags.Of(app).Add("Environment", envName);
Tags.Of(app).Add("ManagedBy", "cdk");

var envLabel = char.ToUpperInvariant(envName[0]) + envName[1..];

var network = new NetworkStack(app, $"AppPlatform-{envLabel}-Network", new NetworkStackProps
{
    EnvName = envName,
    Env = awsEnv,
    Description = $"Shared VPC, ALB, and ECS cluster for the {envName} environment",
});

foreach (var definition in AppDefinitions.All)
{
    var imageOverride = app.Node.TryGetContext($"{definition.Name}Image")?.ToString();
    var resolved = string.IsNullOrWhiteSpace(imageOverride)
        ? definition
        : definition with { ImageUri = imageOverride };

    _ = new AppStack(app, $"AppPlatform-{envLabel}-{definition.Name}", new AppStackProps
    {
        EnvName = envName,
        App = resolved,
        Network = network,
        Env = awsEnv,
        Description = $"{definition.Name} API, Postgres, and React frontend ({envName})",
    });
}

app.Synth();
