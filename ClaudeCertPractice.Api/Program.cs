using ClaudeCertPractice.Api.Configuration;
using ClaudeCertPractice.Api.Endpoints;
using ClaudeCertPractice.Api.Services;

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
builder.Services.Configure<AdminSettings>(builder.Configuration.GetSection(AdminSettings.SectionName));
builder.Services.AddSingleton<AdminService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Claude Cert Practice API", Version = "v1" });
});
builder.Services.AddSingleton<ExamGuideService>();
builder.Services.AddSingleton<QuestionBankService>();
builder.Services.AddSingleton<QuizSessionService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddHttpClient<LearningContentService>();
builder.Services.AddHttpClient<AiQuestionGeneratorService>();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Claude Cert Practice API v1");
    });
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapQuizEndpoints();
app.MapUserEndpoints();
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
