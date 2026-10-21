using System.Security.Claims;
using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using StockEngine.Api.Hubs;
using StockEngine.Api.Models;
using StockEngine.Api.Security;
using StockEngine.Api.Services;
using StockEngine.Api.Services.Payments;

// DB kolonları snake_case (physical_stock), C# özellikleri PascalCase (PhysicalStock).
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
using var bootLogFactory = LoggerFactory.Create(b => b.AddConsole());
var bootLogger = bootLogFactory.CreateLogger("Startup");

builder.Services.Configure<ReservationOptions>(config.GetSection("Reservation"));

// ---- güvenlik: alan şifreleme anahtarı ve JWT imza anahtarı
var encryptionKey = SecretProvider.GetOrCreateKey(config, "Encryption:Key", 32, "encryption.key", builder.Environment.ContentRootPath, bootLogger);
var jwtKey = SecretProvider.GetOrCreateKey(config, "Jwt:Key", 64, "jwt.key", builder.Environment.ContentRootPath, bootLogger);
var jwtOptions = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var jwt = new JwtTokenService(jwtKey, jwtOptions);

builder.Services.AddSingleton<IEncryptionService>(new AesGcmEncryptionService(encryptionKey));
builder.Services.AddSingleton(jwt);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.Key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        // WebSocket/SSE isteklerinde tarayıcı Authorization header gönderemez; token query string'den alınır.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
            // Rol, yetkiler ve aktiflik token'dan değil veritabanından alınır (users + staff_users):
            // yetki değişiklikleri ve devre dışı bırakılan hesaplar mevcut token'lar için de hemen geçerli olur.
            OnTokenValidated = async ctx =>
            {
                var sub = ctx.Principal?.FindFirst("sub")?.Value;
                if (!Guid.TryParse(sub, out var userId) || ctx.Principal?.Identity is not ClaimsIdentity identity)
                {
                    ctx.Fail("Geçersiz token.");
                    return;
                }

                var access = await ctx.HttpContext.RequestServices.GetRequiredService<UserAccessService>().GetAsync(userId);
                if (access is null || !access.IsActive)
                {
                    ctx.Fail("Hesap bulunamadı veya devre dışı.");
                    return;
                }

                foreach (var claim in identity.FindAll(c => c.Type is "role" or "perm").ToList())
                    identity.RemoveClaim(claim);
                identity.AddClaim(new Claim("role", access.Role));
                foreach (var p in access.Permissions)
                    identity.AddClaim(new Claim("perm", p));
            }
        };
    });

builder.Services.AddAuthorization(o =>
{
    // Her yetki için aynı adlı bir politika: ADMIN veya ilgili "perm" claim'i.
    foreach (var permission in Permissions.Assignable)
        o.AddPolicy(permission, b => b.RequireAuthenticatedUser()
            .RequireAssertion(c => c.User.IsInRole(Roles.Admin) || c.User.HasClaim("perm", permission)));

    // Kullanıcı/yetki yönetimi devredilemez.
    o.AddPolicy(Permissions.UsersManage, b => b.RequireAuthenticatedUser().RequireRole(Roles.Admin));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

// ---- uygulama servisleri
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<UserAccessService>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<IInventoryService, MySqlInventoryService>();
builder.Services.AddSingleton<IStockNotifier, SignalRStockNotifier>();
builder.Services.AddSingleton<IPaymentGateway, MockPaymentGateway>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ShopService>();
builder.Services.AddHostedService<ReservationExpiryWorker>();

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    if (ctx.Request.Path.StartsWithSegments("/api"))
        ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHub<StockHub>(StockHub.Path);
app.MapGet("/", () => Results.Redirect("/swagger"));

try
{
    var products = await app.Services.GetRequiredService<IInventoryService>().GetProductsAsync();
    app.Logger.LogInformation("MySQL bağlantısı başarılı. {Count} ürün bulundu.", products.Count);
    await app.Services.GetRequiredService<AdminService>().EnsureAdminAsync(config, app.Environment.ContentRootPath);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Veritabanı hazır değil. veritabani.md içindeki SQL'i (yeni tablolar dahil) çalıştırdığınızdan ve bağlantı cümlesinin doğru olduğundan emin olun.");
}

app.Run();
