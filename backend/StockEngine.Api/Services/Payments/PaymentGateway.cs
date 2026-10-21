namespace StockEngine.Api.Services.Payments;

public static class CardHelper
{
    public static string Digits(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    public static bool PassesLuhn(string pan)
    {
        var sum = 0;
        var alt = false;
        for (var i = pan.Length - 1; i >= 0; i--)
        {
            var d = pan[i] - '0';
            if (alt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            alt = !alt;
        }
        return sum % 10 == 0;
    }

    public static string Brand(string pan)
    {
        if (pan.StartsWith("9792")) return "Troy";
        if (pan.StartsWith('4')) return "Visa";
        if (pan.StartsWith("34") || pan.StartsWith("37")) return "Amex";
        if (pan.Length >= 4 && int.TryParse(pan[..4], out var p4) && p4 is >= 2221 and <= 2720) return "Mastercard";
        if (pan.Length >= 2 && int.TryParse(pan[..2], out var p2) && p2 is >= 51 and <= 55) return "Mastercard";
        return "Kart";
    }
}

public record PaymentRequest(decimal Amount, string Pan, string HolderName, int ExpMonth, int ExpYear, string Cvv);
public record PaymentResult(bool Approved, string Reference, string? DeclineReason);

public interface IPaymentGateway
{
    Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default);
    Task VoidAsync(string reference, CancellationToken ct = default);
}

/// <summary>
/// Gerçek para hareketi yapmayan test ödeme geçidi. Canlıda iyzico/Stripe vb. bir sağlayıcıyla değiştirilmelidir;
/// kart verisi doğrudan sağlayıcıya gönderilir ve sunucuda yalnızca token saklanır.
/// Test kartları: 4000000000000002 → reddedilir, 4000000000009995 → yetersiz bakiye; diğer geçerli kartlar onaylanır.
/// </summary>
public class MockPaymentGateway : IPaymentGateway
{
    private readonly ILogger<MockPaymentGateway> _logger;

    public MockPaymentGateway(ILogger<MockPaymentGateway> logger) => _logger = logger;

    public async Task<PaymentResult> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        await Task.Delay(300, ct); // sağlayıcı gecikmesi simülasyonu

        if (request.Pan == "4000000000000002")
            return new PaymentResult(false, "", "Kartınız bankanız tarafından reddedildi.");
        if (request.Pan == "4000000000009995")
            return new PaymentResult(false, "", "Kart bakiyesi yetersiz.");

        var reference = $"MOCK-{Guid.NewGuid():N}"[..21].ToUpperInvariant();
        _logger.LogInformation("Test ödemesi onaylandı: {Reference} {Amount:0.00} TRY", reference, request.Amount);
        return new PaymentResult(true, reference, null);
    }

    public Task VoidAsync(string reference, CancellationToken ct = default)
    {
        _logger.LogWarning("Test ödemesi iptal edildi (void): {Reference}", reference);
        return Task.CompletedTask;
    }
}
