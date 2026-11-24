using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.S3;
using Constructs;
using AppPlatform.Infra.Configuration;

namespace AppPlatform.Infra.Constructs;

public sealed class ReactFrontendProps
{
    public required AppDefinition App { get; init; }
    public required string EnvName { get; init; }
    public required IApplicationLoadBalancer LoadBalancer { get; init; }
}

public sealed class ReactFrontend : Construct
{
    public Bucket Bucket { get; }

    public Distribution Distribution { get; }

    public ReactFrontend(Construct scope, string id, ReactFrontendProps props) : base(scope, id)
    {
        var app = props.App;
        var spaRewrite = new Function(this, "SpaRewrite", new FunctionProps
        {
            Comment = $"Serve index.html for {app.Name} routes that are not files",
            Code = FunctionCode.FromInline(
                """
                function handler(event) {
                    var request = event.request;
                    if (request.uri.indexOf('.') === -1) {
                        request.uri = '/index.html';
                    }
                    return request;
                }
                """),
            Runtime = FunctionRuntime.JS_2_0,
        });

        Bucket = new Bucket(this, "Bucket", new BucketProps
        {
            BucketName = null,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            RemovalPolicy = RemovalPolicy.DESTROY,
            AutoDeleteObjects = true,
        });

        Distribution = new Distribution(this, "Distribution", new DistributionProps
        {
            Comment = $"{app.Name} React frontend ({props.EnvName})",
            DefaultRootObject = "index.html",
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(Bucket),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
                FunctionAssociations =
                [
                    new FunctionAssociation
                    {
                        Function = spaRewrite,
                        EventType = FunctionEventType.VIEWER_REQUEST,
                    },
                ],
            },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                [$"{app.PathPrefix}/*"] = new BehaviorOptions
                {
                    Origin = new HttpOrigin(props.LoadBalancer.LoadBalancerDnsName, new HttpOriginProps
                    {
                        ProtocolPolicy = OriginProtocolPolicy.HTTP_ONLY,
                        HttpPort = 80,
                    }),
                    ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                    AllowedMethods = AllowedMethods.ALLOW_ALL,
                    CachePolicy = CachePolicy.CACHING_DISABLED,
                    OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
                },
            },
        });
        Distribution.ApplyRemovalPolicy(RemovalPolicy.DESTROY);
    }
}
