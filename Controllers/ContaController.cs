using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Sistemas_para_pericias.Data;
using Sistemas_para_pericias.Models;

namespace Sistemas_para_pericias.Controllers;

[Route("Conta")]
public class ContaController(PericiasContext db, IPasswordHasher<ContaUsuario> passwordHasher) : Controller
{
    [HttpGet("Entrar"), AllowAnonymous]
    public IActionResult Entrar(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpGet("Cadastro"), AllowAnonymous]
    public IActionResult Cadastro() => View(new CadastroContaViewModel());

    [HttpPost("Cadastro"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Cadastro(CadastroContaViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var email = model.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.ContasUsuarios.AnyAsync(conta => conta.EmailNormalizado == normalizedEmail))
        {
            ModelState.AddModelError(nameof(model.Email), "Este e-mail já possui uma conta.");
            return View(model);
        }

        var conta = new ContaUsuario
        {
            Email = email,
            EmailNormalizado = normalizedEmail,
            Nome = model.Nome.Trim(),
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        conta.SenhaHash = passwordHasher.HashPassword(conta, model.Senha);
        db.ContasUsuarios.Add(conta);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(model.Email), "Não foi possível criar a conta. Confira se este e-mail já está cadastrado.");
            return View(model);
        }

        await IniciarSessao(conta);
        return RedirectToAction("Index", "Assinatura");
    }

    [HttpPost("Entrar"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Entrar(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model);

        var normalizedEmail = model.Email.Trim().ToUpperInvariant();
        var conta = await db.ContasUsuarios.SingleOrDefaultAsync(item => item.EmailNormalizado == normalizedEmail);
        var resultado = conta is { Ativo: true }
            ? passwordHasher.VerifyHashedPassword(conta, conta.SenhaHash, model.Senha)
            : PasswordVerificationResult.Failed;

        if (conta is null || !conta.Ativo || resultado == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "E-mail ou senha incorretos.");
            return View(model);
        }

        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
        {
            conta.SenhaHash = passwordHasher.HashPassword(conta, model.Senha);
            await db.SaveChangesAsync();
        }

        await IniciarSessao(conta);

        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl!) : RedirectToAction("Index", "Home");
    }

    [HttpGet("AlterarSenha")]
    public IActionResult AlterarSenha() => View(new AlterarSenhaViewModel());

    [HttpPost("AlterarSenha"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AlterarSenha(AlterarSenhaViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Challenge();
        var conta = await db.ContasUsuarios.SingleOrDefaultAsync(item => item.Id == userId && item.Ativo);
        if (conta is null) return Challenge();
        if (passwordHasher.VerifyHashedPassword(conta, conta.SenhaHash, model.SenhaAtual) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(model.SenhaAtual), "A senha atual não confere.");
            return View(model);
        }
        conta.SenhaHash = passwordHasher.HashPassword(conta, model.NovaSenha);
        await db.SaveChangesAsync();
        TempData["MensagemSenha"] = "Senha alterada. Entre novamente com a nova senha.";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Entrar));
    }

    private async Task IniciarSessao(ContaUsuario conta)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, conta.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, conta.Nome),
            new Claim(ClaimTypes.Email, conta.Email),
            new Claim("nome", conta.Nome)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    [HttpPost("Sair"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Entrar));
    }
}
