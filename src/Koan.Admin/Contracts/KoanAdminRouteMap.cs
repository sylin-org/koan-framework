namespace Koan.Admin.Contracts;

public sealed record KoanAdminRouteMap(string Prefix, string RootTemplate, string ApiTemplate)
{
    public string RootPath => "/" + RootTemplate;
    public string ApiPath => "/" + ApiTemplate;
    public string StatusTemplate => ApiTemplate + "/status";
    public string StatusPath => "/" + StatusTemplate;
    public string ManifestTemplate => ApiTemplate + "/manifest";
    public string ManifestPath => "/" + ManifestTemplate;
    public string LaunchKitTemplate => ApiTemplate + "/launchkit";
    public string LaunchKitPath => "/" + LaunchKitTemplate;
    public string HealthTemplate => ApiTemplate + "/health";
    public string HealthPath => "/" + HealthTemplate;
    public string LogStreamTemplate => ApiTemplate + "/logs";
    public string LogStreamPath => "/" + LogStreamTemplate;
}
