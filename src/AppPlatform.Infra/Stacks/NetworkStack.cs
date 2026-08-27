using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Constructs;

namespace AppPlatform.Infra.Stacks;

public sealed class NetworkStackProps : StackProps
{
    public required string EnvName { get; init; }
}

/// <summary>
/// Shared network for one environment: VPC, NAT, internet-facing ALB, and ECS cluster.
/// App stacks attach API target groups to <see cref="HttpListener"/>.
/// </summary>
public sealed class NetworkStack : Stack
{
    public IVpc Vpc { get; }

    public Cluster Cluster { get; }

    public ApplicationLoadBalancer Alb { get; }

    public ApplicationListener HttpListener { get; }

    public NetworkStack(Construct scope, string id, NetworkStackProps props) : base(scope, id, props)
    {
        var cidr = props.EnvName == "dev" ? "10.0.0.0/16" : "10.1.0.0/16";

        Vpc = new Vpc(this, "Vpc", new VpcProps
        {
            IpAddresses = IpAddresses.Cidr(cidr),
            MaxAzs = 2,
            NatGateways = 1,
            SubnetConfiguration =
            [
                new SubnetConfiguration
                {
                    Name = "public",
                    SubnetType = SubnetType.PUBLIC,
                    CidrMask = 24,
                },
                new SubnetConfiguration
                {
                    Name = "private",
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    CidrMask = 24,
                },
            ],
        });

        Cluster = new Cluster(this, "Cluster", new ClusterProps
        {
            ClusterName = $"app-platform-{props.EnvName}",
            Vpc = Vpc,
            ContainerInsightsV2 = ContainerInsights.ENABLED,
        });
        Cluster.ApplyRemovalPolicy(RemovalPolicy.DESTROY);

        Alb = new ApplicationLoadBalancer(this, "Alb", new ApplicationLoadBalancerProps
        {
            LoadBalancerName = $"app-plat-{props.EnvName}",
            Vpc = Vpc,
            InternetFacing = true,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
            DeletionProtection = false,
        });

        HttpListener = Alb.AddListener("Http", new BaseApplicationListenerProps
        {
            Port = 80,
            Protocol = ApplicationProtocol.HTTP,
            Open = true,
            DefaultAction = ListenerAction.FixedResponse(404, new FixedResponseOptions
            {
                ContentType = "text/plain",
                MessageBody = "Not Found",
            }),
        });

        new CfnOutput(this, "VpcId", new CfnOutputProps
        {
            Value = Vpc.VpcId,
            Description = "Environment VPC",
        });
        new CfnOutput(this, "AlbDnsName", new CfnOutputProps
        {
            Value = Alb.LoadBalancerDnsName,
            Description = "Shared ALB DNS. APIs are at /{app} and /{app}/* (constflow, proflow).",
        });
        new CfnOutput(this, "ClusterName", new CfnOutputProps
        {
            Value = Cluster.ClusterName,
            Description = "ECS cluster name",
        });
    }
}
