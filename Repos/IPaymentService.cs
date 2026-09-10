using IZIPay.Models;
using IZIPay.Services;
using System.Text.Json;

namespace IZIPay.Repos
{
    public interface IPaymentService
    {
        Task<PaymentRecord> CreatePendingPaymentAsync(Product product, string provider, CancellationToken cancellationToken);
        Task SetCheckoutIdAsync(PaymentRecord payment, string checkoutId, CancellationToken cancellationToken);

        Task<bool> UpdatePaymentByCheckoutIdAsync(string checkoutId, string status, CancellationToken cancellationToken);

        Task<GatewayCheckoutResult> CreateGatewayCheckoutAsync(string provider, PaymentRecord payment, CancellationToken cancellationToken);

        Task<SlickPayInvoiceResult> CreateSlickPayInvoiceAsync(PaymentRecord payment, Product product, CancellationToken cancellationToken);

        string? FindCheckoutId(JsonElement root);

        string? FindInvoiceId(JsonElement root);

        string? FindUrl(JsonElement element);
    }
}
