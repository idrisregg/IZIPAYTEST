using IZIPay.Data;
using IZIPay.Models;
using IZIPay.Repos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IZIPay.Services;

public sealed class PaymentService(
    IziPayDbContext db,
    IOptions<GatewayOptions> gatewayOptions,
    IHttpClientFactory httpClientFactory) : IPaymentService
{
    private readonly GatewayOptions gateway = gatewayOptions.Value;
    private readonly IHttpClientFactory httpClientFactory = httpClientFactory;

    public async Task<PaymentRecord> CreatePendingPaymentAsync(
        Product product,
        string provider,
        CancellationToken cancellationToken)
    {
        var payment = new PaymentRecord
        {
            PlanId = product.Name,
            Provider = provider,
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

    public async Task<SlickPayInvoiceResult> CreateSlickPayInvoiceAsync(
        PaymentRecord payment,
        Product product,
        CancellationToken cancellationToken)
    {
        var settings = gateway.SlickPay;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) ||
            settings.ApiKey.StartsWith("replace-with-", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.ApiUrl) ||
            string.IsNullOrWhiteSpace(settings.Customer.Firstname) ||
            string.IsNullOrWhiteSpace(settings.Customer.Lastname) ||
            string.IsNullOrWhiteSpace(settings.Customer.Phone) ||
            string.IsNullOrWhiteSpace(settings.Customer.Email) ||
            string.IsNullOrWhiteSpace(settings.Customer.Address))
        {
            throw new InvalidOperationException("SlickPay gateway configuration is incomplete.");
        }

        var client = httpClientFactory.CreateClient("Gateway");
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            amount = product.Amount,
            url = settings.ReturnUrl,
            firstname = settings.Customer.Firstname,
            lastname = settings.Customer.Lastname,
            phone = settings.Customer.Phone,
            email = settings.Customer.Email,
            address = settings.Customer.Address,
            items = new[]
            {
                new { name = product.Name, price = product.Amount, quantity = 1 }
            }
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GatewayApiException(response.StatusCode, responseBody);
        }

        using var document = JsonDocument.Parse(responseBody);
        return new SlickPayInvoiceResult(
            FindInvoiceId(document.RootElement),
            FindUrl(document.RootElement));
    }


    public string? FindCheckoutId(JsonElement root)
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

    public string? FindInvoiceId(JsonElement root)
    {
        string[][] paths =
        [
            ["data", "id"],
            ["data", "invoice_id"],
            ["data", "invoice", "id"],
            ["id"],
            ["invoice_id"],
            ["invoice", "id"]
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

            if (found && (value.ValueKind == JsonValueKind.String ||
                          value.ValueKind == JsonValueKind.Number))
            {
                var invoiceId = value.ToString();
                if (!string.IsNullOrWhiteSpace(invoiceId))
                {
                    return invoiceId;
                }
            }
        }

        return null;
    }

    public string? FindUrl(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("url", out var dataUrl) &&
                dataUrl.ValueKind == JsonValueKind.String)
            {
                return dataUrl.GetString();
            }

            if (element.TryGetProperty("url", out var url) &&
                url.ValueKind == JsonValueKind.String)
            {
                return url.GetString();
            }

            foreach (var property in element.EnumerateObject())
            {
                var nestedUrl = FindUrl(property.Value);
                if (!string.IsNullOrWhiteSpace(nestedUrl))
                {
                    return nestedUrl;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nestedUrl = FindUrl(item);
                if (!string.IsNullOrWhiteSpace(nestedUrl))
                {
                    return nestedUrl;
                }
            }
        }

        return null;
    }
}

public sealed record SlickPayInvoiceResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("data")] SlickPayInvoiceData? Data);

public sealed record SlickPayInvoiceData(
    [property: System.Text.Json.Serialization.JsonPropertyName("url")] string? Url);

public sealed record GatewayCheckoutResult(string? CheckoutId, string? Url);

public sealed record SlickPayInvoiceResult(string? InvoiceId, string? Url);

public sealed class GatewayApiException(HttpStatusCode statusCode, string responseBody) : Exception
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}
