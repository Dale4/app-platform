using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Constructs;
using AppPlatform.Infra.Configuration;
using AppPlatform.Infra.Constructs;

namespace AppPlatform.Infra.Stacks;

public sealed class AppStackProps : StackProps
{
    public required string EnvName { get; init; }
    public required AppDefinition App { get; init; }
    public required NetworkStack Network { get; init; }
}

/// <summary>
/// Per-app resources in an environment: RDS Postgres, ECS Fargate API, ECR, and S3 + CloudFront.
/// </summary>
public sealed class AppStack : Stack
{
    public AppStack(Construct scope, string id, AppStackProps props) : base(scope, id, props)
    {
        var app = props.App;
        var network = props.Network;

        var database = new PostgresDatabase(this, "Database", new PostgresDatabaseProps
        {
            App = app,
            EnvName = props.EnvName,
            Vpc = network.Vpc,
        });

        var repository = new Repository(this, "Ecr", new RepositoryProps
        {
            RepositoryName = $"app-platform/{props.EnvName}/{app.Id}",
            ImageScanOnPush = true,
            RemovalPolicy = RemovalPolicy.DESTROY,
            EmptyOnDelete = true,
        });

        var api = new AspNetFargateService(this, "Api", new AspNetFargateServiceProps
        {
            App = app,
            EnvName = props.EnvName,
            Cluster = network.Cluster,
            Vpc = network.Vpc,
            DatabaseSecret = database.Secret,
            DatabaseHost = database.Instance.DbInstanceEndpointAddress,
            DatabasePort = database.Instance.DbInstanceEndpointPort,
            DatabaseName = app.DatabaseName,
        });

        database.Instance.Connections.AllowFrom(api.SecurityGroup, Port.Tcp(5432), "Fargate API to Postgres");
        api.SecurityGroup.Connections.AllowFrom(network.Alb, Port.Tcp(app.ResolvedPort), "ALB to Fargate API");

        // Target group + listener rule live in this stack so the network stack does not depend on apps.
        var targetGroup = new ApplicationTargetGroup(this, "ApiTargets", new ApplicationTargetGroupProps
        {
            Vpc = network.Vpc,
            Port = app.ResolvedPort,
            Protocol = ApplicationProtocol.HTTP,
            TargetType = TargetType.IP,
            DeregistrationDelay = Duration.Seconds(30),
            HealthCheck = new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
            {
                Path = app.ResolvedHealthPath,
                HealthyHttpCodes = "200-399",
                Interval = Duration.Seconds(30),
                Timeout = Duration.Seconds(5),
                HealthyThresholdCount = 2,
                UnhealthyThresholdCount = 3,
            },
        });
        api.Service.AttachToApplicationTargetGroup(targetGroup);

        _ = new ApplicationListenerRule(this, "ApiRule", new ApplicationListenerRuleProps
        {
            Listener = network.HttpListener,
            Priority = app.ListenerPriority,
            Conditions = [ListenerCondition.PathPatterns([app.PathPrefix, $"{app.PathPrefix}/*"])],
            TargetGroups = [targetGroup],
        });

        var frontend = new ReactFrontend(this, "Frontend", new ReactFrontendProps
        {
            App = app,
            EnvName = props.EnvName,
        });

        new CfnOutput(this, "EcrUri", new CfnOutputProps
        {
            Value = repository.RepositoryUri,
            Description = $"Push {app.Name} API images here, then redeploy with -c {app.Name}Image=<uri>:<tag>",
        });
        new CfnOutput(this, "ApiUrl", new CfnOutputProps
        {
            Value = $"http://{network.Alb.LoadBalancerDnsName}{app.PathPrefix}",
            Description = $"{app.Name} API base URL on the shared ALB",
        });
        new CfnOutput(this, "RdsEndpoint", new CfnOutputProps
        {
            Value = database.Instance.InstanceEndpoint.Hostname,
            Description = $"{app.Name} Postgres endpoint (private)",
        });
        new CfnOutput(this, "FrontendBucket", new CfnOutputProps
        {
            Value = frontend.Bucket.BucketName,
            Description = $"Upload the {app.Name} React build (index.html + assets) here",
        });
        new CfnOutput(this, "FrontendUrl", new CfnOutputProps
        {
            Value = $"https://{frontend.Distribution.DistributionDomainName}",
            Description = $"{app.Name} CloudFront URL",
        });
    }
}
