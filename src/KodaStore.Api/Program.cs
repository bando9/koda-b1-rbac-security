using KodaStore.Infrastructure.Persistence;
using Scalar.AspNetCore;
using KodaStore.Api.Files;
using KodaStore.Api.Middlewares;
using KodaStore.Application.Repositories;
using KodaStore.Application.Services;
using Microsoft.EntityFrameworkCore;
using KodaStore.Infrastructure.Seeding;
using KodaStore.Infrastructure.Repositories;
using KodaStore.Infrastructure;
using KodaStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using KodaStore.Application.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(
    option =>
    {
        option.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;

            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        };
    }
);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var jwtOption = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Konfigurasi JWT belum tersedia");

if(string.IsNullOrWhiteSpace(jwtOption.Issuer)) 
        throw new InvalidOperationException("Jwt: Issuer must be present.");
if(string.IsNullOrWhiteSpace(jwtOption.Audience)) 
        throw new InvalidOperationException("Jwt: Audience must be present.");
if(jwtOption.AccessTokenMinutes is < 1 or > 60) 
        throw new InvalidOperationException("Jwt: Access Token Minutes antara 1-60 detik.");

byte[] signingKeyBytes;
try
{
    signingKeyBytes = Convert.FromBase64String(jwtOption.SigningKeyBase64);
}
catch (FormatException e)
{
    throw new InvalidOperationException("Jwt: SigningKeyBase64 tidak menggunakan Base 64 yang valid",e);
}

// HS256
if(signingKeyBytes.Length < 32) throw new InvalidOperationException("Jwt signing key minimal 32 byte");
 


// // Service
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IProductImageStorage, LocalProductImageStorage>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductQueryService, ProductQueryService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();


builder.Services.AddInfrastructure(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("ecommercedb") 
    ?? throw new InvalidOperationException("ga bisa koneksi, belum konfigurasi");

// Membaca daftar origin di env.Development
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

// Daftarkan Configuration CORS
builder.Services.AddCors(option =>
{
    option.AddPolicy("ReactClient", policy =>
    {
        policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});


builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString)
        .UseSeeding((context, _) =>
        {
            ProductSeedData.Seed(context);
             OrderSeedData.Seed(context);
        })
        .UseAsyncSeeding(async (context,_,ct) =>
        {
            await ProductSeedData.SeedAsync(context,ct);
            await OrderSeedData.SeedAsync(context,ct);
        });

});

builder.Services.AddRateLimiter(option =>
{
    option.AddPolicy("auth-login", context=>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "Nihil";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"login: {ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit=5, // 5x
                Window=TimeSpan.FromMinutes(3), // kena softbanned 3 menit
                QueueLimit=0,
                AutoReplenishment=true
            }
        );
    });

    option.AddPolicy("auth-register", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "Nihil";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"register: {ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit=5,
                Window=TimeSpan.FromMinutes(10),
                QueueLimit=0,
                AutoReplenishment=true
            }
        );  
    });

    // Refresh halaman
    option.AddPolicy("auth-refresh", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "Nihil";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"refresh: {ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit=10,
                Window=TimeSpan.FromMinutes(2),
                QueueLimit=0,
                AutoReplenishment=true
            }
        );  
    });
    
});


// *Authentication
builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(option =>
{
    option.MapInboundClaims = false;
    option.IncludeErrorDetails = false;
    option.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtOption.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOption.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
        RequireSignedTokens = true,
        ValidAlgorithms = [
            SecurityAlgorithms.HmacSha256
        ],
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = JwtRegisteredClaimNames.Sub,
        RoleClaimType = "role"
    };
});


// *Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.CanManageCategory, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(AppRoles.Admin);
    });

    options.AddPolicy(AppPolicies.CanManageProduct, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(AppRoles.Admin);
    });
});


var app = builder.Build();
app.UseExceptionHandler();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    var userManager = scope.ServiceProvider
        .GetRequiredService<UserManager<AppUser>>();

    var bootstrapAdminEmail = app.Configuration[
        "RoleBootstrap:AdminEmail"];

    await RoleSeedData.SeedAsync(
        roleManager,
        userManager,
        bootstrapAdminEmail);
        
    app.MapOpenApi();

    app.MapScalarApiReference("/api");

    app.UseSwaggerUI(options => // UseSwaggerUI is called only in Development.
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Koda Store API v1");
        options.RoutePrefix = "swagger";

        options.DocumentTitle = "Koda Store | API Documentation";

        options.EnableFilter();
    });
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// *apply cors ReactClient policy
app.UseMiddleware<RequestTimingMiddleware>();
app.UseCors("ReactClient");

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();
 

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();