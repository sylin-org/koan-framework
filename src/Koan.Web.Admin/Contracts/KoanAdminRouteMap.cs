namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminRouteMap(string Prefix, string RootTemplate)
{
    public string RootPath => "/" + RootTemplate;
    public string StatusTemplate => RootTemplate + "/status";
    public string StatusPath => "/" + StatusTemplate;
    public string HealthTemplate => RootTemplate + "/health";
    public string HealthPath => "/" + HealthTemplate;
}
