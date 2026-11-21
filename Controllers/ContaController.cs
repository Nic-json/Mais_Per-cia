using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Sistemas_para_pericias.Data;
using Sistemas_para_pericias.Models;
using Sistemas_para_pericias.Models.Requests;
using Sistemas_para_pericias.Models.Responses;
using Sistemas_para_pericias.Services;

namespace Sistemas_para_pericias.Controllers;

[Route("Conta")]
public class ContaController(
    PericiasContext db,
    IPasswordHasher<ContaUsuario> passwordHasher,
    IAccountEmailSender emailSender,
    IConfiguration configuration,
    ILogger<ContaController> logger) : Controller
{
    private const string FinalidadeConfirmacao = "confirmar-email";
    private const string FinalidadeRedefinicao = "redefinir-senha";

    [HttpGet("Entrar"), AllowAnonymous]
    public IActionResult Entrar(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpGet("Cadastro"), AllowAnonymous]
    public IActionResult Cadastro() { ViewBag.EmailDisponivel = CadastroConfigurado(); return View(new CadastroContaViewModel()); }

    [HttpPost("Cadastro"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Cadastro(CadastroContaViewModel model)
    {
        ViewBag.EmailDisponivel = CadastroConfigurado();
        if (!CadastroConfigurado())
        {
            ModelState.AddModelError(string.Empty, "O cadastro está temporariamente indisponível enquanto o e-mail de envio, o contato de suporte e a identificação do serviço não estiverem configurados.");
            return View(model);
        }
        if (!ModelState.IsValid) return View(model);

        var email = model.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.ContasUsuarios.AnyAsync(conta => conta.EmailNormalizado == normalizedEmail))
        {
            ModelState.AddModelError(string.Empty, "Não foi possível criar a conta com esses dados. Se você já se cadastrou, confirme seu e-mail ou recupere sua senha.");
            return View(model);
        }

        var conta = new ContaUsuario
        {
            Email = email,
            EmailNormalizado = normalizedEmail,
            Nome = model.Nome.Trim(),
            Ativo = true,
            EmailConfirmado = false,
            CriadoEm = DateTime.UtcNow,
            UltimoAcessoEm = DateTime.UtcNow
        };
        conta.SenhaHash = passwordHasher.HashPassword(conta, model.Senha);
        db.ContasUsuarios.Add(conta);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "Não foi possível criar a conta com esses dados. Se você já se cadastrou, confirme seu e-mail ou recupere sua senha.");
            return View(model);
        }

        try { await EnviarTokenAsync(conta, FinalidadeConfirmacao, TimeSpan.FromHours(24), "Confirme seu e-mail", "Confirme seu e-mail para ativar sua conta.", nameof(ConfirmarEmail)); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no envio da confirmação de e-mail para a conta {UsuarioId}.", conta.Id);
            TempData["AvisoConta"] = "A conta foi criada, mas não conseguimos enviar o e-mail agora. Use Reenviar confirmação para tentar novamente.";
            return RedirectToAction(nameof(ReenviarConfirmacao));
        }
        return RedirectToAction(nameof(ConfirmacaoEnviada));
    }

    [HttpGet("ConfirmacaoEnviada"), AllowAnonymous]
    public IActionResult ConfirmacaoEnviada() => View();

    [HttpGet("ReenviarConfirmacao"), AllowAnonymous]
    public IActionResult ReenviarConfirmacao() => View(new SolicitarRedefinicaoSenhaViewModel());

    [HttpPost("ReenviarConfirmacao"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> ReenviarConfirmacao(SolicitarRedefinicaoSenhaViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (emailSender.IsConfigured)
        {
            var email = model.Email.Trim().ToUpperInvariant();
            var conta = await db.ContasUsuarios.SingleOrDefaultAsync(u => u.EmailNormalizado == email && u.Ativo && !u.EmailConfirmado);
            if (conta is not null)
            {
                try { await EnviarTokenAsync(conta, FinalidadeConfirmacao, TimeSpan.FromHours(24), "Confirme seu e-mail", "Confirme seu e-mail para ativar sua conta.", nameof(ConfirmarEmail)); }
                catch (Exception exception) { logger.LogError(exception, "Falha ao reenviar confirmação para a conta {UsuarioId}.", conta.Id); }
            }
        }
        TempData["MensagemConta"] = "Se houver uma conta ativa ainda não confirmada com esse e-mail, enviaremos um novo link.";
        return RedirectToAction(nameof(ReenviarConfirmacao));
    }

    [HttpGet("ConfirmarEmail"), AllowAnonymous]
    public async Task<IActionResult> ConfirmarEmail(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || await ObterTokenAsync(token, FinalidadeConfirmacao) is null)
            return View("LinkExpirado");
        return View("ConfirmarEmail", token);
    }

    [HttpPost("ConfirmarEmail"), AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarEmailPost(string token)
    {
        var registro = await ObterTokenAsync(token, FinalidadeConfirmacao);
        if (registro is null) return View("LinkExpirado");
        var conta = await db.ContasUsuarios.SingleOrDefaultAsync(u => u.Id == registro.UsuarioId && u.Ativo);
        if (conta is null) return View("LinkExpirado");
        var agora = DateTime.UtcNow;
        var consumido = await db.TokensConta.Where(t => t.Hash == registro.Hash && t.UsadoEm == null && t.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora));
        if (consumido != 1) return View("LinkExpirado");
        conta.EmailConfirmado = true;
        await db.SaveChangesAsync();
        return View("EmailConfirmado");
    }

    [HttpGet("EsqueciSenha"), AllowAnonymous]
    public IActionResult EsqueciSenha() => View(new SolicitarRedefinicaoSenhaViewModel());

    [HttpPost("EsqueciSenha"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> EsqueciSenha(SolicitarRedefinicaoSenhaViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (emailSender.IsConfigured)
        {
            var email = model.Email.Trim().ToUpperInvariant();
            var conta = await db.ContasUsuarios.SingleOrDefaultAsync(u => u.EmailNormalizado == email && u.Ativo && u.EmailConfirmado);
            if (conta is not null)
            {
                try { await EnviarTokenAsync(conta, FinalidadeRedefinicao, TimeSpan.FromHours(1), "Redefina sua senha", "Use o link abaixo para escolher uma nova senha. O link expira em uma hora.", nameof(RedefinirSenha)); }
                catch (Exception exception) { logger.LogError(exception, "Falha no envio de redefinição para a conta {UsuarioId}.", conta.Id); }
            }
        }
        TempData["MensagemConta"] = "Se encontrarmos uma conta confirmada com esse e-mail, enviaremos instruções para redefinir a senha.";
        return RedirectToAction(nameof(EsqueciSenha));
    }

    [HttpGet("RedefinirSenha"), AllowAnonymous]
    public async Task<IActionResult> RedefinirSenha(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return View("LinkExpirado");
        var registro = await ObterTokenAsync(token, FinalidadeRedefinicao);
        if (registro is null || !await db.ContasUsuarios.AnyAsync(u => u.Id == registro.UsuarioId && u.Ativo && u.EmailConfirmado))
            return View("LinkExpirado");
        return View(new RedefinirSenhaViewModel { Token = token });
    }

    [HttpPost("RedefinirSenha"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> RedefinirSenha(RedefinirSenhaViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var registro = await ObterTokenAsync(model.Token, FinalidadeRedefinicao);
        var conta = registro is null ? null : await db.ContasUsuarios.SingleOrDefaultAsync(u => u.Id == registro.UsuarioId && u.Ativo && u.EmailConfirmado);
        if (registro is null || conta is null) return View("LinkExpirado");
        var agora = DateTime.UtcNow;
        var consumido = await db.TokensConta.Where(t => t.Hash == registro.Hash && t.UsadoEm == null && t.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora));
        if (consumido != 1) return View("LinkExpirado");
        conta.SenhaHash = passwordHasher.HashPassword(conta, model.NovaSenha);
        await db.TokensConta.Where(t => t.UsuarioId == conta.Id && t.Finalidade == FinalidadeRedefinicao && t.UsadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora));
        await db.SaveChangesAsync();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["MensagemSenha"] = "Senha redefinida. Entre com a nova senha.";
        return RedirectToAction(nameof(Entrar));
    }

    [HttpPost("Entrar"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Entrar(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) { ViewData["ReturnUrl"] = returnUrl; return View(model); }
        var normalizedEmail = model.Email.Trim().ToUpperInvariant();
        var conta = await db.ContasUsuarios.SingleOrDefaultAsync(item => item.EmailNormalizado == normalizedEmail);
        var resultado = conta is { Ativo: true }
            ? passwordHasher.VerifyHashedPassword(conta, conta.SenhaHash, model.Senha)
            : PasswordVerificationResult.Failed;

        if (conta is null || !conta.Ativo || resultado == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "E-mail ou senha incorretos.");
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }
        if (!conta.EmailConfirmado)
        {
            ModelState.AddModelError(string.Empty, "Confirme seu e-mail antes de entrar. Você pode solicitar um novo link de confirmação.");
            ViewData["ReturnUrl"] = returnUrl;
            return View(model);
        }
        conta.UltimoAcessoEm = DateTime.UtcNow;
        conta.AvisoInatividadeEnviadoEm = null;
        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
            conta.SenhaHash = passwordHasher.HashPassword(conta, model.Senha);
        await db.SaveChangesAsync();
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
        await db.TokensConta.Where(t => t.UsuarioId == conta.Id && t.Finalidade == FinalidadeRedefinicao && t.UsadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, DateTime.UtcNow));
        await db.SaveChangesAsync();
        TempData["MensagemSenha"] = "Senha alterada. Entre novamente com a nova senha.";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Entrar));
    }

    private async Task EnviarTokenAsync(ContaUsuario conta, string finalidade, TimeSpan validade, string assunto, string chamada, string acao)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = HashToken(token);
        var agora = DateTime.UtcNow;
        await db.TokensConta.Where(t => t.UsuarioId == conta.Id && t.Finalidade == finalidade && t.UsadoEm == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora));
        db.TokensConta.Add(new TokenConta { Hash = hash, UsuarioId = conta.Id, Finalidade = finalidade, ExpiraEm = agora.Add(validade) });
        await db.SaveChangesAsync();
        var link = Url.Action(acao, "Conta", new { token }, Request.Scheme);
        var safeLink = System.Net.WebUtility.HtmlEncode(link);
        var body = $"<p>Olá, {System.Net.WebUtility.HtmlEncode(conta.Nome)}.</p><p>{chamada}</p><p><a href=\"{safeLink}\">Continuar</a></p><p>Se você não solicitou esta ação, ignore esta mensagem.</p>";
        await emailSender.SendAsync(conta.Email, assunto, body, HttpContext.RequestAborted);
    }

    private Task<TokenConta?> ObterTokenAsync(string token, string finalidade)
    {
        if (token.Length is < 20 or > 200) return Task.FromResult<TokenConta?>(null);
        var hash = HashToken(token);
        var agora = DateTime.UtcNow;
        return db.TokensConta.SingleOrDefaultAsync(t => t.Hash == hash && t.Finalidade == finalidade && t.UsadoEm == null && t.ExpiraEm > agora);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private bool CadastroConfigurado() => emailSender.IsConfigured && !string.IsNullOrWhiteSpace(configuration["Service:OperatorName"]);

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

