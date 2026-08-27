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
            },
            ErrorResponses =
            [
                new ErrorResponse
                {
                    HttpStatus = 403,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html",
                    Ttl = Duration.Minutes(1),
                },
                new ErrorResponse
                {
                    HttpStatus = 404,
                    ResponseHttpStatus = 200,
                    ResponsePagePath = "/index.html",
                    Ttl = Duration.Minutes(1),
                },
            ],
        });
        Distribution.ApplyRemovalPolicy(RemovalPolicy.DESTROY);
    }
}
