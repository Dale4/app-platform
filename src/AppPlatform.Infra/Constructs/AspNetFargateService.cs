using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;
using AppPlatform.Infra.Configuration;
using Secret = Amazon.CDK.AWS.ECS.Secret;
using SmSecret = Amazon.CDK.AWS.SecretsManager.Secret;

namespace AppPlatform.Infra.Constructs;

public sealed class AspNetFargateServiceProps
{
    public required AppDefinition App { get; init; }
    public required string EnvName { get; init; }
    public required ICluster Cluster { get; init; }
    public required IVpc Vpc { get; init; }
    public required ISecret DatabaseSecret { get; init; }
    public required string DatabaseHost { get; init; }
    public required string DatabasePort { get; init; }
    public required string DatabaseName { get; init; }
}

public sealed class AspNetFargateService : Construct
{
    /// <summary>
    /// Plaintext secret created by hand per app and environment.
    /// CDK imports it and does not delete it on destroy.
    /// </summary>
    public static string JwtSigningKeySecretName(string envName, string appId) =>
        $"app-platform/{envName}/{appId}/jwt-signing-key";

    public FargateService Service { get; }

    public SecurityGroup SecurityGroup { get; }

    public AspNetFargateService(Construct scope, string id, AspNetFargateServiceProps props) : base(scope, id)
    {
        var app = props.App;
        var logGroup = new LogGroup(this, "Logs", new LogGroupProps
        {
            LogGroupName = $"/app-platform/{props.EnvName}/{app.Id}",
            Retention = RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        var taskDefinition = new FargateTaskDefinition(this, "TaskDef", new FargateTaskDefinitionProps
        {
            Cpu = app.Cpu,
            MemoryLimitMiB = app.MemoryMiB,
            RuntimePlatform = new RuntimePlatform
            {
                CpuArchitecture = CpuArchitecture.X86_64,
                OperatingSystemFamily = OperatingSystemFamily.LINUX,
            },
        });
        taskDefinition.ApplyRemovalPolicy(RemovalPolicy.DESTROY);

        var aspnetEnvironment = props.EnvName == "dev" ? "Development" : "Staging";

        // Imported, not owned by this stack — cdk destroy leaves the secret in place.
        var jwtSigningKey = SmSecret.FromSecretNameV2(
            this, "JwtSigningKey", JwtSigningKeySecretName(props.EnvName, app.Id));
        jwtSigningKey.GrantRead(taskDefinition.ObtainExecutionRole());

        var container = taskDefinition.AddContainer("Api", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry(app.ResolvedImage),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                LogGroup = logGroup,
                StreamPrefix = app.Id,
            }),
            Environment = new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = aspnetEnvironment,
                ["ASPNETCORE_PATHBASE"] = app.PathPrefix,
                ["ASPNETCORE_URLS"] = $"http://+:{app.ResolvedPort}",
                ["DB_HOST"] = props.DatabaseHost,
                ["DB_PORT"] = props.DatabasePort,
                ["DB_NAME"] = props.DatabaseName,
                ["JWT_ISSUER"] = $"app-platform/{props.EnvName}/{app.Id}",
                ["JWT_AUDIENCE"] = $"{app.Id}-api",
            },
            Secrets = new Dictionary<string, Secret>
            {
                ["DB_USER"] = Secret.FromSecretsManager(props.DatabaseSecret, "username"),
                ["DB_PASSWORD"] = Secret.FromSecretsManager(props.DatabaseSecret, "password"),
                ["JWT_SIGNING_KEY"] = Secret.FromSecretsManager(jwtSigningKey),
            },
        });
        container.AddPortMappings(new PortMapping
        {
            ContainerPort = app.ResolvedPort,
            Protocol = Amazon.CDK.AWS.ECS.Protocol.TCP,
        });

        SecurityGroup = new SecurityGroup(this, "Sg", new SecurityGroupProps
        {
            Vpc = props.Vpc,
            Description = $"Fargate tasks for {app.Name} ({props.EnvName})",
            AllowAllOutbound = true,
        });

        Service = new FargateService(this, "Service", new FargateServiceProps
        {
            ServiceName = $"app-platform-{props.EnvName}-{app.Id}",
            Cluster = props.Cluster,
            TaskDefinition = taskDefinition,
            DesiredCount = 1,
            AssignPublicIp = false,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            SecurityGroups = [SecurityGroup],
            CircuitBreaker = new DeploymentCircuitBreaker { Rollback = true },
            EnableExecuteCommand = true,
            MinHealthyPercent = 0,
            MaxHealthyPercent = 200,
        });
    }
}
