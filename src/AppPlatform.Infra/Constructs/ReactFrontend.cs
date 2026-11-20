using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.S3;
using Constructs;
using AppPlatform.Infra.Configuration;

namespace AppPlatform.Infra.Constructs;

public sealed class ReactFrontendProps
{
    public required AppDefinition App { get; init; }
    public required string EnvName { get; init; }
    public required string AlbDnsName { get; init; }
}

public sealed class ReactFrontend : Construct
{
    public Bucket Bucket { get; }

    public Distribution Distribution { get; }

    public ReactFrontend(Construct scope, string id, ReactFrontendProps props) : base(scope, id)
    {
        var app = props.App;

        Bucket = new Bucket(this, "Bucket", new BucketProps
        {
            BucketName = null,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            RemovalPolicy = RemovalPolicy.DESTROY,
            AutoDeleteObjects = true,
        });

        // Distribution-wide error responses would also rewrite API 403/404s.
        // This function runs only on the S3 behavior.
        var spaRewrite = new Function(this, "SpaRewrite", new FunctionProps
        {
            Comment = $"Rewrite {app.Name} client routes to index.html",
            Code = FunctionCode.FromInline(
                """
                function handler(event) {
                    var request = event.request;
                    var uri = request.uri;
                    if (uri.lastIndexOf('.') <= uri.lastIndexOf('/')) {
                        request.uri = '/index.html';
                    }
                    return request;
                }
                """),
        });

        var albOrigin = new HttpOrigin(props.AlbDnsName, new HttpOriginProps
        {
            ProtocolPolicy = OriginProtocolPolicy.HTTP_ONLY,
            HttpPort = 80,
        });

        BehaviorOptions ApiBehavior() => new()
        {
            Origin = albOrigin,
            ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
            AllowedMethods = AllowedMethods.ALLOW_ALL,
            CachePolicy = CachePolicy.CACHING_DISABLED,
            OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
        };

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
                [app.PathPrefix] = ApiBehavior(),
                [$"{app.PathPrefix}/*"] = ApiBehavior(),
            },
        });
        Distribution.ApplyRemovalPolicy(RemovalPolicy.DESTROY);
    }
}
