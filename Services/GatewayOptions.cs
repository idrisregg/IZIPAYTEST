namespace IZIPay;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public GatewaySettings Chargily { get; set; } = new();
    public GatewaySettings SlickPay { get; set; } = new();
}

public sealed class GatewaySettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string FailureUrl { get; set; } = string.Empty;
}
