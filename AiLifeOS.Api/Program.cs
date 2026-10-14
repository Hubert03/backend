using AiLifeOS.Api.Data;
using AiLifeOS.Api.Models;
using AiLifeOS.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;

var configureOpenRouter = args.Contains("--configure-openrouter", StringComparer.Ordinal);
var bootstrapAdminIndex = Array.IndexOf(args, "--bootstrap-admin");
var bootstrapAdminEmail = bootstrapAdminIndex >= 0
    ? bootstrapAdminIndex + 1 < args.Length
        ? args[bootstrapAdminIndex + 1]
        : throw new ArgumentException("--bootstrap-admin requires an email address.")
    : null;
if (configureOpenRouter && bootstrapAdminEmail is not null)
    throw new ArgumentException("Run --configure-openrouter and --bootstrap-admin separately.");

var builderArgs = args
    .Where((argument, index) =>
        argument != "--configure-openrouter" &&
        argument != "--bootstrap-admin" &&
        !(bootstrapAdminIndex >= 0 && index == bootstrapAdminIndex + 1))
    .ToArray();
var builder = WebApplication.CreateBuilder(builderArgs);

// Add services to the container.

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<OpenRouterPlanService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});
var connectionString = builder.Configuration.GetConnectionString("AiLifeOS")
    ?? throw new InvalidOperationException("Connection string 'AiLifeOS' is not configured.");
builder.Services.AddDbContext<AiLifeOsDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AiLifeOsDbContext>()
    .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("is_admin", "true")
        .RequireClaim("is_active", "true"));
});
builder.Services.AddSignalR();
builder.Services.Configure<InvitationEmailOptions>(
    builder.Configuration.GetSection("Email"));
builder.Services.AddTransient<IInvitationEmailSender, SmtpInvitationEmailSender>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = ".AiLifeOS.AntiForgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".AiLifeOS.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // 👈 CRITICAL: Allows cookies/antiforgery
    });
});
var app = builder.Build();

if (configureOpenRouter)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AiLifeOsDbContext>();
    await OpenRouterSettingsConfigurator.ConfigureAsync(dbContext);
    return;
}

if (bootstrapAdminEmail is not null)
{
    await BootstrapAdminAsync(app.Services, bootstrapAdminEmail);
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.UseHttpsRedirection();

app.UseCors("AllowAngularDev"); 

app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.User);
        if (user is null || !user.IsActive)
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var isAdminClaim = context.User.FindFirstValue("is_admin");
        var isActiveClaim = context.User.FindFirstValue("is_active");
        if (isAdminClaim != (user.IsAdmin ? "true" : "false") ||
            isActiveClaim != "true")
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (user.MustChangePassword &&
            (context.Request.Path.StartsWithSegments("/api") ||
             context.Request.Path.StartsWithSegments("/hubs")) &&
            !IsAllowedDuringPasswordChange(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Change your temporary password before using the workspace.",
                mustChangePassword = true,
            });
            return;
        }
    }

    await next();
});
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();

app.Run();

static bool IsAllowedDuringPasswordChange(HttpRequest request) =>
    request.Path.Equals("/api/auth/csrf", StringComparison.OrdinalIgnoreCase) ||
    request.Path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase) ||
    request.Path.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase) ||
    request.Path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase);

static async Task BootstrapAdminAsync(IServiceProvider services, string emailArgument)
{
    var email = emailArgument.Trim();
    if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || email.Length > 256)
        throw new ArgumentException("Provide a valid email address of 256 characters or fewer.");

    await using var scope = services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AiLifeOsDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    if (await userManager.Users.AnyAsync(user => user.IsAdmin))
        throw new InvalidOperationException("An administrator already exists; bootstrap is a one-time operation.");

    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        Console.Write("Initial administrator display name: ");
        var displayName = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length is < 2 or > 100)
            throw new ArgumentException("Display name must contain 2 to 100 characters.");

        var password = ReadPassword("Initial administrator password: ");
        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow,
            IsAdmin = true,
            IsActive = true,
        };
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(string.Join(" ", createResult.Errors.Select(error => error.Description)));

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.UserProfile_Upsert @UserId={user.Id}, @Email={email}, @DisplayName={displayName}, @CreatedAtUtc={user.CreatedAtUtc}");
    }
    else
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.UserAdmin_SetAccess @UserId={user.Id}, @IsAdmin={true}, @IsActive={true}, @MustChangePassword={false}");
    }

    await transaction.CommitAsync();
    Console.WriteLine($"Administrator bootstrap completed for {email}.");
}

static string ReadPassword(string prompt)
{
    if (Console.IsInputRedirected)
        throw new InvalidOperationException("Admin bootstrap requires an interactive console for password entry.");

    Console.Write(prompt);
    var password = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Length--;
                Console.Write("\b \b");
            }
            continue;
        }
        if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
            Console.Write('*');
        }
    }
    Console.WriteLine();
    return password.ToString();
}
