using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;
using AppPlatform.Infra.Configuration;

namespace AppPlatform.Infra.Constructs;

public sealed class PostgresDatabaseProps
{
    public required AppDefinition App { get; init; }
    public required string EnvName { get; init; }
    public required IVpc Vpc { get; init; }
}

public sealed class PostgresDatabase : Construct
{
    public DatabaseInstance Instance { get; }

    public ISecret Secret => Instance.Secret
        ?? throw new InvalidOperationException("RDS instance did not produce a secret.");

    public SecurityGroup SecurityGroup { get; }

    public PostgresDatabase(Construct scope, string id, PostgresDatabaseProps props) : base(scope, id)
    {
        var app = props.App;

        SecurityGroup = new SecurityGroup(this, "Sg", new SecurityGroupProps
        {
            Vpc = props.Vpc,
            Description = $"Postgres for {app.Name} ({props.EnvName})",
            AllowAllOutbound = true,
        });

        Instance = new DatabaseInstance(this, "Instance", new DatabaseInstanceProps
        {
            Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps
            {
                Version = PostgresEngineVersion.VER_16,
            }),
            InstanceType = Amazon.CDK.AWS.EC2.InstanceType.Of(InstanceClass.BURSTABLE4_GRAVITON, InstanceSize.MICRO),
            InstanceIdentifier = $"app-platform-{props.EnvName}-{app.Id}",
            Vpc = props.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            SecurityGroups = [SecurityGroup],
            Credentials = Credentials.FromGeneratedSecret("app"),
            DatabaseName = app.DatabaseName,
            AllocatedStorage = 20,
            MaxAllocatedStorage = 50,
            StorageType = StorageType.GP3,
            MultiAz = false,
            PubliclyAccessible = false,
            DeletionProtection = false,
            RemovalPolicy = RemovalPolicy.DESTROY,
            BackupRetention = Duration.Days(0),
            DeleteAutomatedBackups = true,
            StorageEncrypted = true,
        });
    }
}
