using IZIPay.Data;
using IZIPay.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace IZIPay;

public sealed class PaymentService(
    IziPayDbContext db,
    IOptions<GatewayOptions> gatewayOptions,
    IHttpClientFactory httpClientFactory)
{
    private readonly GatewayOptions gateway = gatewayOptions.Value;
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;

    public async Task<PaymentRecord?> CreatePendingPaymentAsync(
        Product product,
        CancellationToken cancellationToken)
    {
        var payment = new PaymentRecord
        {
            PlanId = product.Name,
            Provider = "chargily",
            Amount = product.Amount,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        return payment;
    }

    public async Task SetCheckoutIdAsync(
        PaymentRecord payment,
        string checkoutId,
        CancellationToken cancellationToken)
    {
        payment.CheckoutId = checkoutId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdatePaymentByCheckoutIdAsync(
        string checkoutId,
        string status,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(
            item => item.CheckoutId == checkoutId && item.Provider == "chargily",
            cancellationToken);

        if (payment is null)
        {
            return false;
        }

        payment.Status = status;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<GatewayCheckoutResult> CreateGatewayCheckoutAsync(
        string provider,
        PaymentRecord payment,
        CancellationToken cancellationToken)
    {
        var settings = provider == "chargily" ? gateway.Chargily : gateway.SlickPay;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) ||
            settings.ApiKey.StartsWith("replace-with-", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.ApiUrl))
        {
            throw new InvalidOperationException($"{provider} gateway configuration is incomplete.");
        }

        var client = httpClientFactory.CreateClient("Gateway");
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            amount = payment.Amount,
            currency = "dzd",
            success_url = settings.ReturnUrl,
            metadata = new { plan_id = payment.PlanId, payment_id = payment.Id }
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GatewayApiException(response.StatusCode, responseBody);
        }

        using var document = JsonDocument.Parse(responseBody);
        return new GatewayCheckoutResult(FindCheckoutId(document.RootElement), null);
    }


    private static string? FindCheckoutId(JsonElement root)
    {
        string[][] paths =
        [
            ["id"],
            ["checkout_id"],
            ["data", "id"],
            ["data", "checkout_id"],
            ["checkout", "id"]
        ];

        foreach (var path in paths)
        {
            var value = root;
            var found = true;

            foreach (var property in path)
            {
                if (value.ValueKind != JsonValueKind.Object ||
                    !value.TryGetProperty(property, out value))
                {
                    found = false;
                    break;
                }
            }

            if (found && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }
}

public sealed record SlickPayCheckoutResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("data")] SlickPayCheckoutData? Data);

public sealed record SlickPayCheckoutData(
    [property: System.Text.Json.Serialization.JsonPropertyName("url")] string? Url);

public sealed record GatewayCheckoutResult(string? CheckoutId, string? Url);

public sealed class GatewayApiException(HttpStatusCode statusCode, string responseBody) : Exception
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}
