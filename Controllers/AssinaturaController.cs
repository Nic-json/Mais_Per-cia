using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sistemas_para_pericias.Data;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Controllers;

[Route("Assinatura")]
public sealed class AssinaturaController(PericiasContext db, IConfiguration config, IHttpClientFactory clients, ILogger<AssinaturaController> logger) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var id = UserId();
        var sub = await db.Assinaturas.SingleOrDefaultAsync(x => x.UsuarioId == id);
        var model = new AssinaturaViewModel
        {
            ValorMensal = decimal.TryParse(config["Billing:MonthlyPrice"], CultureInfo.InvariantCulture, out var price) && price > 0 ? price : null,
            Status = sub?.Status ?? "Pendente",
            AcessoAte = sub?.AcessoAte,
            CheckoutUrl = sub?.CheckoutId is { Length: > 0 } checkoutId
                ? sub.CheckoutLink is { Length: > 0 } savedLink && IsAllowedCheckoutUrl(savedLink)
                    ? savedLink
                    : CheckoutFallbackUrl(checkoutId, config["Asaas:BaseUrl"])
                : null,
            CheckoutConfigurado = CredenciaisOk() && decimal.TryParse(config["Billing:MonthlyPrice"], CultureInfo.InvariantCulture, out var monthly) && monthly > 0
        };
        return View(model);
    }

    [HttpPost("Iniciar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Iniciar()
    {
        var priceText = config["Billing:MonthlyPrice"];
        if (!CredenciaisOk() || !decimal.TryParse(priceText, CultureInfo.InvariantCulture, out var price) || price <= 0)
        {
            TempData["ErroAssinatura"] = "A assinatura ainda não está configurada. Tente novamente mais tarde.";
            return RedirectToAction(nameof(Index));
        }

        var userId = UserId();
        var existing = await db.Assinaturas.SingleOrDefaultAsync(x => x.UsuarioId == userId);
        if (existing?.AcessoAte >= DateTime.UtcNow)
            return RedirectToAction("Index", "Home");

        // Reuse an open checkout. Creating another recurring checkout here could
        // leave two valid subscriptions for the same account.
        if (existing?.Status == "Checkout aberto" && existing.CheckoutId is { Length: > 0 } openCheckoutId)
        {
            var openCheckoutUrl = existing.CheckoutLink is { Length: > 0 } savedLink && IsAllowedCheckoutUrl(savedLink)
                ? savedLink
                : CheckoutFallbackUrl(openCheckoutId, config["Asaas:BaseUrl"]);
            if (!string.IsNullOrWhiteSpace(openCheckoutUrl)) return Redirect(openCheckoutUrl);
        }

        var baseUrl = config["Asaas:BaseUrl"]?.TrimEnd('/') ?? "https://api-sandbox.asaas.com/v3";
        var site = config["Public:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        if (!await GarantirWebhookAsync(baseUrl, site))
        {
            TempData["ErroAssinatura"] = "Não foi possível configurar a confirmação automática do pagamento. Nenhuma cobrança foi iniciada; tente novamente mais tarde.";
            return RedirectToAction(nameof(Index));
        }

        var payload = new
        {
            billingTypes = new[] { "CREDIT_CARD" },
            chargeTypes = new[] { "RECURRENT" },
            minutesToExpire = 1440,
            externalReference = userId.ToString(CultureInfo.InvariantCulture),
            callback = new { successUrl = $"{site}/Assinatura/Retorno", cancelUrl = $"{site}/Assinatura", expiredUrl = $"{site}/Assinatura" },
            items = new[] { new { name = "Assinatura Perícia em Dia", description = "Acesso mensal à Perícia em Dia", quantity = 1, value = price } },
            subscription = new { cycle = "MONTHLY", nextDueDate = Sistemas_para_pericias.Utilities.BrasiliaTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), description = "Assinatura mensal Perícia em Dia", externalReference = userId.ToString(CultureInfo.InvariantCulture) }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/checkouts") { Content = JsonContent.Create(payload) };
            request.Headers.Add("access_token", config["Asaas:ApiKey"]);
            request.Headers.UserAgent.ParseAdd("PericiaEmDia/1.0");
            using var response = await clients.CreateClient("Asaas").SendAsync(request, HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
                var errors = SummarizeApiErrors(responseBody);
                logger.LogWarning("Asaas checkout creation returned HTTP {StatusCode}: {Errors}", (int)response.StatusCode, errors);
                if (errors.Contains("criação do checkout está desabilitada", StringComparison.OrdinalIgnoreCase) ||
                    errors.Contains("regularize a situação cadastral", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["ErroCadastroAsaas"] = true;
                    TempData["ErroAssinatura"] = "O Asaas ainda não liberou novas assinaturas para a conta que recebe os pagamentos. Verifique a situação cadastral dessa conta no painel Asaas e tente novamente após a regularização. Nenhuma cobrança foi gerada nesta tentativa.";
                }
                else
                {
                    TempData["ErroAssinatura"] = "Não foi possível iniciar o pagamento agora. Nenhuma cobrança foi confirmada nesta tentativa. Tente novamente mais tarde.";
                }
                return RedirectToAction(nameof(Index));
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(HttpContext.RequestAborted));
            if (!json.RootElement.TryGetProperty("id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString()))
                throw new InvalidOperationException("Asaas response did not contain checkout id.");
            var checkoutId = idElement.GetString()!;
            var checkoutUrl = GetCheckoutUrl(json.RootElement, checkoutId, baseUrl);
            existing ??= await db.Assinaturas.SingleOrDefaultAsync(x => x.UsuarioId == userId, HttpContext.RequestAborted);
            existing ??= new AssinaturaConta { UsuarioId = userId };
            existing.CheckoutId = checkoutId;
            existing.CheckoutLink = checkoutUrl;
            existing.Status = "Checkout aberto";
            existing.AtualizadoEm = DateTime.UtcNow;
            if (db.Entry(existing).State == EntityState.Detached) db.Assinaturas.Add(existing);
            await db.SaveChangesAsync(HttpContext.RequestAborted);
            return Redirect(checkoutUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogError(ex, "Failed to create Asaas checkout for account {AccountId}", userId);
            TempData["ErroAssinatura"] = "Não foi possível abrir o checkout agora. Tente novamente mais tarde.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet("Retorno")]
    public IActionResult Retorno()
    {
        TempData["RetornoAssinatura"] = "Checkout encerrado. Se o pagamento foi concluído, a confirmação chegará pelo Asaas em instantes; atualize a situação nesta tela.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/webhooks/asaas"), AllowAnonymous, IgnoreAntiforgeryToken, RequestSizeLimit(131072)]
    public async Task<IActionResult> Webhook()
    {
        var expected = config["Asaas:WebhookToken"];
        var supplied = Request.Headers["asaas-access-token"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || !SameSecret(expected, supplied)) return Unauthorized();

        JsonDocument doc;
        try { doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: HttpContext.RequestAborted); }
        catch (JsonException) { return BadRequest(); }
        using (doc)
        {
            var root = doc.RootElement;
            var eventId = String(root, "id");
            var eventName = String(root, "event");
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventName)) return BadRequest();
            if (await db.EventosAsaas.AnyAsync(e => e.Id == eventId)) return Ok();

            var checkout = root.TryGetProperty("checkout", out var checkoutObj) ? checkoutObj : default;
            var subscription = root.TryGetProperty("subscription", out var subObj) ? subObj : default;
            var payment = root.TryGetProperty("payment", out var payObj) ? payObj : default;
            var checkoutId = String(checkout, "id") ?? String(payment, "checkoutSession");
            var subscriptionId = String(subscription, "id") ?? String(payment, "subscription");
            var customerId = String(checkout, "customer") ?? String(subscription, "customer") ?? String(payment, "customer");
            var externalRef = String(checkout, "externalReference") ?? String(subscription, "externalReference") ?? String(payment, "externalReference");
            var uid = int.TryParse(externalRef, out var parsed) ? parsed : (int?)null;
            var accountSub = await db.Assinaturas.FirstOrDefaultAsync(s =>
                (checkoutId != null && s.CheckoutId == checkoutId) ||
                (subscriptionId != null && s.SubscriptionId == subscriptionId) ||
                (customerId != null && s.CustomerId == customerId) ||
                (uid.HasValue && s.UsuarioId == uid.Value));
            if (accountSub is null && uid.HasValue && await db.ContasUsuarios.AnyAsync(u => u.Id == uid.Value && u.Ativo))
            {
                accountSub = new AssinaturaConta { UsuarioId = uid.Value };
                db.Assinaturas.Add(accountSub);
            }
            if (accountSub is null) return StatusCode(500); // retry after local checkout record is available

            if (checkoutId is not null) accountSub.CheckoutId = checkoutId;
            if (subscriptionId is not null) accountSub.SubscriptionId = subscriptionId;
            if (customerId is not null) accountSub.CustomerId = customerId;
            accountSub.AtualizadoEm = DateTime.UtcNow;
            if (eventName == "CHECKOUT_PAID")
            {
                accountSub.Status = "Ativa";
                accountSub.AcessoAte = Max(accountSub.AcessoAte, DateTime.UtcNow.AddDays(35));
            }
            else if (eventName == "PAYMENT_CONFIRMED" || eventName == "PAYMENT_RECEIVED")
            {
                var paidAt = ParseDate(String(payment, "paymentDate") ?? String(payment, "clientPaymentDate")) ?? DateTime.UtcNow;
                accountSub.Status = "Ativa";
                accountSub.AcessoAte = Max(accountSub.AcessoAte, paidAt.AddDays(35));
            }
            else if (eventName is "PAYMENT_REFUNDED" or "PAYMENT_PARTIALLY_REFUNDED" or "PAYMENT_CHARGEBACK_REQUESTED" or "PAYMENT_RECEIVED_IN_CASH_UNDONE")
            {
                accountSub.Status = "Suspensa";
                accountSub.AcessoAte = DateTime.UtcNow;
            }
            else if (eventName is "PAYMENT_OVERDUE" or "PAYMENT_CREDIT_CARD_CAPTURE_REFUSED"
                && (accountSub.AcessoAte is null || accountSub.AcessoAte < DateTime.UtcNow))
            {
                accountSub.Status = "Pendente";
            }
            else if (eventName is "CHECKOUT_CANCELED" or "CHECKOUT_EXPIRED")
            {
                if (accountSub.AcessoAte is null || accountSub.AcessoAte < DateTime.UtcNow)
                    accountSub.Status = eventName == "CHECKOUT_CANCELED" ? "Checkout cancelado" : "Checkout expirado";
            }
            else if (eventName is "SUBSCRIPTION_CREATED" or "SUBSCRIPTION_UPDATED")
                accountSub.Status = accountSub.AcessoAte >= DateTime.UtcNow ? "Ativa" : "Pagamento pendente";
            else if (eventName is "SUBSCRIPTION_INACTIVATED" or "SUBSCRIPTION_DELETED") accountSub.Status = "Cancelada";

            db.EventosAsaas.Add(new EventoAsaas { Id = eventId });
            try
            {
                await db.SaveChangesAsync(HttpContext.RequestAborted);
            }
            catch (DbUpdateException)
            {
                // A entrega pode ser repetida enquanto outro pedido grava o mesmo ID.
                if (await db.EventosAsaas.AnyAsync(e => e.Id == eventId, HttpContext.RequestAborted)) return Ok();
                throw;
            }
            return Ok();
        }
    }

    private int UserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    private bool CredenciaisOk() =>
        !string.IsNullOrWhiteSpace(config["Asaas:ApiKey"]) &&
        config["Asaas:WebhookToken"] is { Length: >= 32 and <= 255 } token &&
        !token.Any(char.IsWhiteSpace);

    private async Task<bool> GarantirWebhookAsync(string baseUrl, string site)
    {
        var token = config["Asaas:WebhookToken"]!;
        var email = config["Admin:Email"];
        if (string.IsNullOrWhiteSpace(email)) return false;
        var url = $"{site}/webhooks/asaas";
        var body = new
        {
            name = "Perícia em Dia - Assinaturas",
            url,
            email,
            enabled = true,
            interrupted = false,
            apiVersion = 3,
            authToken = token,
            sendType = "SEQUENTIALLY",
            events = new[]
            {
                "CHECKOUT_CREATED", "CHECKOUT_PAID", "CHECKOUT_CANCELED", "CHECKOUT_EXPIRED",
                "SUBSCRIPTION_CREATED", "SUBSCRIPTION_UPDATED", "SUBSCRIPTION_INACTIVATED", "SUBSCRIPTION_DELETED",
                "PAYMENT_CONFIRMED", "PAYMENT_RECEIVED", "PAYMENT_OVERDUE", "PAYMENT_REFUNDED",
                "PAYMENT_PARTIALLY_REFUNDED", "PAYMENT_CREDIT_CARD_CAPTURE_REFUSED",
                "PAYMENT_CHARGEBACK_REQUESTED", "PAYMENT_RECEIVED_IN_CASH_UNDONE"
            }
        };
        var client = clients.CreateClient("Asaas");
        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string endpoint, object? content = null)
        {
            var request = new HttpRequestMessage(method, $"{baseUrl}{endpoint}");
            request.Headers.Add("access_token", config["Asaas:ApiKey"]);
            request.Headers.UserAgent.ParseAdd("PericiaEmDia/1.0");
            if (content is not null) request.Content = JsonContent.Create(content);
            return await client.SendAsync(request, HttpContext.RequestAborted);
        }

        try
        {
            using var listResponse = await SendAsync(HttpMethod.Get, "/webhooks?offset=0&limit=100");
            if (!listResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Asaas webhook list returned HTTP {StatusCode}", (int)listResponse.StatusCode);
                return false;
            }
            using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(HttpContext.RequestAborted));
            string? webhookId = null;
            if (listJson.RootElement.TryGetProperty("data", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (String(item, "url") == url)
                    {
                        webhookId = String(item, "id");
                        break;
                    }
                }
            }

            using var saveResponse = webhookId is null
                ? await SendAsync(HttpMethod.Post, "/webhooks", body)
                : await SendAsync(HttpMethod.Put, $"/webhooks/{Uri.EscapeDataString(webhookId)}", body);
            if (saveResponse.IsSuccessStatusCode) return true;
            logger.LogWarning("Asaas webhook provisioning returned HTTP {StatusCode}", (int)saveResponse.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogError(ex, "Failed to provision Asaas webhook");
            return false;
        }
    }

    private static string GetCheckoutUrl(JsonElement checkout, string id, string baseUrl)
    {
        if (checkout.TryGetProperty("link", out var linkElement) && linkElement.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(linkElement.GetString(), UriKind.Absolute, out var link) && IsAllowedCheckoutUrl(link.AbsoluteUri))
            return link.AbsoluteUri;

        return CheckoutFallbackUrl(id, baseUrl);
    }

    private static bool IsAllowedCheckoutUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var link) && link.Scheme == Uri.UriSchemeHttps &&
        (link.Host.Equals("sandbox.asaas.com", StringComparison.OrdinalIgnoreCase) || link.Host.Equals("asaas.com", StringComparison.OrdinalIgnoreCase));

    private static string CheckoutFallbackUrl(string id, string? baseUrl)
    {
        var escapedId = Uri.EscapeDataString(id);
        return baseUrl?.Contains("api-sandbox.asaas.com", StringComparison.OrdinalIgnoreCase) == true
            ? $"https://sandbox.asaas.com/000/checkoutSession/show/{escapedId}"
            : $"https://asaas.com/checkoutSession/show?id={escapedId}";
    }
    private static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTime? ParseDate(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result) ? result : null;
    private static DateTime Max(DateTime? current, DateTime proposed) => current is null || proposed > current ? proposed : current.Value;
    private static string SummarizeApiErrors(string responseBody)
    {
        try
        {
            using var json = JsonDocument.Parse(responseBody);
            if (json.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var descriptions = errors.EnumerateArray()
                    .Select(error => String(error, "description") ?? String(error, "message"))
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Take(3)
                    .ToArray();
                if (descriptions.Length > 0)
                {
                    var summary = string.Join(" | ", descriptions);
                    return summary.Length <= 500 ? summary : summary[..500];
                }
            }
        }
        catch (JsonException) { }

        return "No error details returned";
    }
    private static bool SameSecret(string expected, string supplied)
    {
        var a = Encoding.UTF8.GetBytes(expected); var b = Encoding.UTF8.GetBytes(supplied);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
