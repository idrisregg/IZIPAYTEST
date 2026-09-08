using System.ComponentModel.DataAnnotations.Schema;

namespace IZIPay.Models;

[Table("izipay")]
public sealed class PaymentRecord
{
    [Column("id")]
    public int Id { get; set; }

    [Column("plan_id")]
    public required string PlanId { get; set; }

    public required string Provider { get; set; }

    public int Amount { get; set; }

    public required string Status { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("checkout_id")]
    public string? CheckoutId { get; set; }
}
