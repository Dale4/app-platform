namespace AppPlatform.Infra.Configuration;

/// <summary>
/// One product that deploys into a shared environment. Add a new entry here to onboard an app.
/// </summary>
public sealed record AppDefinition(
    string Name,
    int ContainerPort,
    string HealthPath,
    int Cpu,
    int MemoryMiB,
    int ListenerPriority,
    string? ImageUri = null)
{
    public const string PlaceholderImage = "public.ecr.aws/nginx/nginx:stable-alpine";

    public string Id => Name.ToLowerInvariant();

    public string PathPrefix => $"/{Id}";

    public string DatabaseName => $"{Id}_db";

    public bool UsesPlaceholder => string.IsNullOrWhiteSpace(ImageUri);

    public string ResolvedImage => UsesPlaceholder ? PlaceholderImage : ImageUri!;

    /// <summary>Nginx placeholder listens on 80; ASP.NET 8 APIs typically listen on 8080.</summary>
    public int ResolvedPort => UsesPlaceholder ? 80 : ContainerPort;

    /// <summary>Nginx serves <c>/</c>; real APIs should expose <see cref="HealthPath"/>.</summary>
    public string ResolvedHealthPath => UsesPlaceholder ? "/" : HealthPath;
}

public static class AppDefinitions
{
    public static readonly IReadOnlyList<AppDefinition> All =
    [
        new("ConstFlow", ContainerPort: 8080, HealthPath: "/health", Cpu: 256, MemoryMiB: 512, ListenerPriority: 10),
        new("ProFlow", ContainerPort: 8080, HealthPath: "/health", Cpu: 256, MemoryMiB: 512, ListenerPriority: 20),
        new("WaterFlow", ContainerPort: 8080, HealthPath: "/health", Cpu: 256, MemoryMiB: 512, ListenerPriority: 30),
    ];

    /// <summary>
    /// All apps when <paramref name="appName"/> is omitted; otherwise the matching app (name or id, case-insensitive).
    /// </summary>
    public static IReadOnlyList<AppDefinition> Resolve(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return All;

        var match = All.FirstOrDefault(a =>
            a.Name.Equals(appName, StringComparison.OrdinalIgnoreCase) ||
            a.Id.Equals(appName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var names = string.Join(", ", All.Select(a => a.Name));
            throw new ArgumentException(
                $"Context 'app' must be one of: {names}. Example: cdk deploy --all -c env=dev -c app=ConstFlow");
        }

        return [match];
    }
}
