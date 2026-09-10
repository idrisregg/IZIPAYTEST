using IZIPay.Models;
using IZIPay.Repos;
using IZIPay.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IZIPay.Controllers;

[ApiController]
[Route("izipay/slickpay")]
[EnableRateLimiting("Fixed")]
public sealed class SlickPayController(IPaymentService paymentService) : ControllerBase
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

        var payment = await _paymentService.CreatePendingPaymentAsync(
            product, "slickpay", cancellationToken);

        try
        {
            var invoice = await _paymentService.CreateSlickPayInvoiceAsync(
                payment, product, cancellationToken);
            if (string.IsNullOrWhiteSpace(invoice.Url))
            {
                return Problem("SlickPay did not return a payment URL.", statusCode: 502);
            }

            if (string.IsNullOrWhiteSpace(invoice.InvoiceId))
            {
                return Problem("SlickPay did not return an invoice ID.", statusCode: 502);
            }

            await _paymentService.SetCheckoutIdAsync(
                payment, invoice.InvoiceId, cancellationToken);

            return Ok(new { url = invoice.Url, invoiceId = invoice.InvoiceId });
        }
        catch (GatewayApiException exception)
        {
            return Problem(
                $"SlickPay returned {(int)exception.StatusCode}: {exception.ResponseBody}",
                statusCode: 502);
        }
        catch (HttpRequestException)
        {
            return Problem("SlickPay invoice could not be created.", statusCode: 502);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: 500);
        }
    }

}