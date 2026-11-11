using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Sistemas_para_pericias.Data;
using Sistemas_para_pericias.Models;
using Sistemas_para_pericias.Security;
using Ganss.Xss;

var builder = WebApplication.CreateBuilder(args);

var cultura = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("Asaas", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IPasswordHasher<ContaUsuario>, PasswordHasher<ContaUsuario>>();
builder.Services.AddTransient(_ =>
{
    var sanitizer = new HtmlSanitizer();
    sanitizer.AllowedTags.Clear();
    foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "h1", "h2", "h3", "blockquote", "hr", "ul", "ol", "li", "table", "thead", "tbody", "tr", "th", "td", "div" })
        sanitizer.AllowedTags.Add(tag);
    sanitizer.AllowedAttributes.Clear();
    sanitizer.AllowedCssProperties.Clear();
    return sanitizer;
});
var adminEmail = builder.Configuration["Admin:Email"];
var adminPassword = builder.Configuration["Admin:Password"];
if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 16)
    throw new InvalidOperationException("Configure Admin__Email e Admin__Password (mínimo de 16 caracteres) nas variáveis de ambiente antes de iniciar.");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Conta/Entrar";
        options.Cookie.Name = "PericiaClara.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(options => options.AddPolicy("login", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));
var connectionString = builder.Configuration.GetConnectionString("Pericias") ?? "Data Source=App_Data/pericias.db";
var dbFile = new SqliteConnectionStringBuilder(connectionString).DataSource;
if (!string.IsNullOrWhiteSpace(dbFile) && dbFile != ":memory:")
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbFile))!);
var documentosDir = builder.Configuration["Documentos:Diretorio"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "uploads");
Directory.CreateDirectory(documentosDir);
var chavesDataProtectionDir = builder.Configuration["DataProtection:KeysDirectory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(chavesDataProtectionDir);
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(chavesDataProtectionDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(chavesDataProtectionDir))
    .SetApplicationName("PericiaClara");

builder.Services.AddDbContext<PericiasContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownProxies.Add(IPAddress.Parse("172.30.253.2"));
forwardedHeaders.ForwardLimit = 1;
app.UseForwardedHeaders(forwardedHeaders);
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PericiasContext>();
    db.Database.EnsureCreated();
    db.Database.OpenConnection();
    try
    {
        SqliteSchema.EnsureAdditionalTables((SqliteConnection)db.Database.GetDbConnection());
    }
    finally
    {
        db.Database.CloseConnection();
    }

    var seedEmail = adminEmail.Trim();
    var seedNormalizedEmail = seedEmail.ToUpperInvariant();
    if (!await db.ContasUsuarios.AnyAsync(conta => conta.Id == 1))
    {
        if (await db.ContasUsuarios.AnyAsync(conta => conta.EmailNormalizado == seedNormalizedEmail))
            throw new InvalidOperationException("A conta inicial precisa usar o identificador 1 para preservar o vínculo dos dados antigos. Confira a tabela ContaUsuarios.");

        var contaInicial = new ContaUsuario
        {
            Id = 1,
            Email = seedEmail,
            EmailNormalizado = seedNormalizedEmail,
            Nome = seedEmail.Split('@')[0],
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        contaInicial.SenhaHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ContaUsuario>>()
            .HashPassword(contaInicial, adminPassword);
        db.ContasUsuarios.Add(contaInicial);
        await db.SaveChangesAsync();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(cultura),
    SupportedCultures = [cultura],
    SupportedUICultures = [cultura]
});

app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (context.User.Identity?.IsAuthenticated == true &&
        !Path.HasExtension(path.Value) &&
        !path.StartsWithSegments("/css") &&
        !path.StartsWithSegments("/js") &&
        !path.StartsWithSegments("/lib") &&
        !path.StartsWithSegments("/Assinatura") &&
        !path.StartsWithSegments("/Conta") &&
        !path.StartsWithSegments("/webhooks") &&
        !path.StartsWithSegments("/health") &&
        int.TryParse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId) && userId != 1)
    {
        var db = context.RequestServices.GetRequiredService<PericiasContext>();
        var liberado = await db.Assinaturas.AnyAsync(s => s.UsuarioId == userId && s.AcessoAte != null && s.AcessoAte >= DateTime.UtcNow);
        if (!liberado)
        {
            context.Response.Redirect("/Assinatura");
            return;
        }
    }
    await next();
});
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
