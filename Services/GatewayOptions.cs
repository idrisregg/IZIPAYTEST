namespace IZIPay.Services;

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
    public SlickPayCustomer Customer { get; set; } = new();
    public string ReturnUrl { get; set; } = string.Empty;
    public string FailureUrl { get; set; } = string.Empty;
}

public sealed class SlickPayCustomer
{
    public string Firstname { get; set; } = string.Empty;
    public string Lastname { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

