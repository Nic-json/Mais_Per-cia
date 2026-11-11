using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Globalization;
using System.Threading.RateLimiting;
using Sistemas_para_pericias.Data;
using Ganss.Xss;

var builder = WebApplication.CreateBuilder(args);

var cultura = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

// Add services to the container.
builder.Services.AddControllersWithViews();
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

builder.Services.AddDbContext<PericiasContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

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
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
