using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Controllers;

[Route("Conta")]
public class ContaController(IConfiguration configuration) : Controller
{
    [HttpGet("Entrar"), AllowAnonymous]
    public IActionResult Entrar(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("Entrar"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Entrar(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model);

        var emailConfigurado = configuration["Admin:Email"] ?? "";
        var senhaConfigurada = configuration["Admin:Password"] ?? "";
        var emailValido = string.Equals(model.Email.Trim(), emailConfigurado, StringComparison.OrdinalIgnoreCase);
        var senhaTentada = Encoding.UTF8.GetBytes(model.Senha);
        var senhaCorreta = Encoding.UTF8.GetBytes(senhaConfigurada);
        var senhaValida = senhaTentada.Length == senhaCorreta.Length && CryptographicOperations.FixedTimeEquals(senhaTentada, senhaCorreta);

        if (!emailValido || !senhaValida)
        {
            ModelState.AddModelError(string.Empty, "E-mail ou senha incorretos.");
            return View(model);
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, emailConfigurado),
            new Claim(ClaimTypes.Name, emailConfigurado),
            new Claim(ClaimTypes.Email, emailConfigurado)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl!) : RedirectToAction("Index", "Home");
    }

    [HttpPost("Sair"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Entrar));
    }
}
