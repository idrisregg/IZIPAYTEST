using Microsoft.AspNetCore.Mvc;
using IZIPay.Models;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using IZIPay.Services;
using IZIPay.Repos;

namespace IZIPay.Controllers;

[ApiController]
[Route("izipay/chargily")]
[EnableRateLimiting("Fixed")]
public sealed class ChargilyController(IPaymentService paymentService, IConfiguration configuration) : ControllerBase
{
    private readonly IPaymentService _paymentService = paymentService;

    [HttpPost("checkout/{productId:int}")]
    public async Task<IActionResult> Checkout(int productId, CancellationToken cancellationToken)
    {
        var product = ProductCatalog.Find(productId);
        if (product is null)

        {
            return NotFound(new { message = $"Product {productId} was not found." });
        }

        var payment = await _paymentService.CreatePendingPaymentAsync(product, "chargily", cancellationToken);

        try
        {
            var checkout = await _paymentService.CreateGatewayCheckoutAsync(
                "chargily", payment, cancellationToken);

            if (string.IsNullOrWhiteSpace(checkout.CheckoutId))
            {
                return Problem("Chargily did not return a checkout ID.", statusCode: 502);
            }

            await _paymentService.SetCheckoutIdAsync(payment, checkout.CheckoutId, cancellationToken);
            return Ok(new { checkout_id = checkout.CheckoutId });
        }
        catch (HttpRequestException)
        {
            return Problem("Chargily checkout could not be created.", statusCode: 502);
        }
        catch (GatewayApiException exception)
        {
            return Problem(
                $"Chargily returned {(int)exception.StatusCode}: {exception.ResponseBody}",
                statusCode: 502);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: 500);
        }
    }

    [HttpPost("webhook")]
    [DisableRateLimiting]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        var payload = await new StreamReader(Request.Body).ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return BadRequest(new { message = "Missing Chargily signature." });
        }

        if (!VerifySignature(payload, signature, configuration["Gateway:Chargily:SecretKey"]))
        {
            return Forbid();
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var eventType = ReadString(root, "type");
        if (eventType is not ("checkout.paid" or "checkout.failed"))
        {
            return Ok();
        }

        if (!root.TryGetProperty("data", out var checkout))
        {
            return BadRequest(new { message = "Webhook checkout data is missing." });
        }

        var checkoutId = ReadString(checkout, "id");
        if (string.IsNullOrWhiteSpace(checkoutId))
        {
            return BadRequest(new { message = "Webhook checkout ID is missing." });
        }

        var checkoutStatus = ReadString(checkout, "status");

        string stat = "";

        if (eventType == "checkout.paid" && checkoutStatus == "paid")
        {
            stat = "Paid";
        }
        if (eventType == "checkout.failed" && checkoutStatus == "failed")
        {
            stat = "Failed";
        }

        var updated = await _paymentService.UpdatePaymentByCheckoutIdAsync(
            checkoutId,
            stat,
            cancellationToken);

        if (!updated)
        {
            return NotFound();
        }

        return Ok();
    }


    [HttpGet("db")]
    public IActionResult DownloadDatabase()
    {
        var rootPath = AppContext.BaseDirectory;
        var filePath = Path.Combine(rootPath, "izipay.db");

        if (!System.IO.File.Exists(filePath))
            return NotFound($"Database file not found at path: {filePath}");

        var fileBytes = System.IO.File.ReadAllBytes(filePath);
        return File(fileBytes, "application/x-sqlite3", "izipay_backup.db");
    }



    private static bool VerifySignature(string payload, string signature, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) ||
            !TryDecodeHex(signature, out var suppliedSignature))
        {
            return false;
        }

        var expectedSignature = HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret),
            System.Text.Encoding.UTF8.GetBytes(payload));
        return CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature);
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

}
