using System.Text;
using ClaudeCertPractice.Api.Configuration;
using ClaudeCertPractice.Api.Data;
using ClaudeCertPractice.Api.Data.Entities;
using ClaudeCertPractice.Api.Endpoints;
using ClaudeCertPractice.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Render (and similar hosts) inject PORT; bind Kestrel explicitly.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

EnvFileLoader.LoadFromContentRoot(Directory.GetCurrentDirectory());
EnvFileLoader.LoadFromContentRoot(builder.Environment.ContentRootPath);

builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

builder.Services.Configure<QuizSettings>(builder.Configuration.GetSection(QuizSettings.SectionName));
builder.Services.Configure<AuthSettings>(options =>
{
    builder.Configuration.GetSection(AuthSettings.SectionName).Bind(options);
    if (string.IsNullOrWhiteSpace(options.JwtSecret) || options.JwtSecret.Length < 32)
    {
        options.JwtSecret = builder.Configuration["JWT_SECRET"]
            ?? "dev-only-jwt-secret-change-before-production-32chars";
    }
});

var connectionString = DatabaseConnectionResolver.Resolve(builder.Configuration);
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var npgsqlDataSource = dataSourceBuilder.Build();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(npgsqlDataSource));

var jwtSecret = builder.Configuration.GetSection(AuthSettings.SectionName).Get<AuthSettings>()?.JwtSecret;
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    jwtSecret = builder.Configuration["JWT_SECRET"]
        ?? "dev-only-jwt-secret-change-before-production-32chars";
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Claude Cert Practice API", Version = "v1" });
});
builder.Services.AddSingleton<ExamGuideService>();
builder.Services.AddSingleton<QuestionBankService>();
builder.Services.AddSingleton<QuizSessionService>();
builder.Services.AddSingleton<AiGenerationJobService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<LearningContentService>();
builder.Services.AddHttpClient<AiQuestionGeneratorService>(client =>
{
    // Parallel AI batches for a full 60-question exam can take 1–2 minutes.
    client.Timeout = TimeSpan.FromMinutes(3);
});

var corsOrigins = builder.Configuration["CORS_ALLOWED_ORIGINS"];
var allowedOrigins = string.IsNullOrWhiteSpace(corsOrigins)
    ? new[]
    {
        "http://localhost:5173",
        "http://127.0.0.1:5173",
        "http://localhost:5174",
        "http://127.0.0.1:5174"
    }
    : corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

await DbSeeder.InitializeAsync(app.Services, app.Environment);

// Continue any AI generation jobs that were interrupted by a process restart.
_ = Task.Run(async () =>
{
    try
    {
        var jobs = app.Services.GetRequiredService<AiGenerationJobService>();
        await jobs.ResumeInterruptedJobsAsync();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogWarning(ex, "Could not resume interrupted AI generation jobs.");
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Claude Cert Practice API v1");
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapQuizEndpoints();
app.MapUserEndpoints();
app.MapAuthEndpoints();
app.MapAdminEndpoints();

// Development: API + Swagger only. Run the React UI separately (npm run dev in frontend/).
if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}
else
{
    var wwwroot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    var spaIndex = Path.Combine(wwwroot, "index.html");
    if (File.Exists(spaIndex))
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }
    else
    {
        app.MapGet("/", () => Results.Ok(new
        {
            status = "ok",
            service = "ClaudeCertPractice.Api",
            health = "/health",
            api = "/api/quiz/metadata"
        }));
    }
}

app.Run();
