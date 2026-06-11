using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SecureVote.Authentication;
using SecureVote.Entities;
using SecureVote.Extensions;
using SecureVote.Persistence;
using SecureVote.Services;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add JWT Authentication FIRST (before Identity)
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidAudience = jwtOptions.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };
});

// Add Identity
builder.Services.AddIdentity<User, Microsoft.AspNetCore.Identity.IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

// Override Identity's authentication schemes with JWT
builder.Services.ConfigureApplicationCookie(options =>
{
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

// Force JWT as default scheme (Identity overrides it, so we PostConfigure)
builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

builder.Services.AddHttpContextAccessor();

// Add FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Add Services
builder.Services.AddScoped<IJwtProvider, JwtProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IElectionService, ElectionService>();
builder.Services.AddScoped<ICandidateService, CandidateService>();
builder.Services.AddScoped<IElectionOrganizerService, ElectionOrganizerService>();
builder.Services.AddScoped<IVoterUploadService, VoterUploadService>();
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
// Add HttpClient for face recognition
builder.Services.AddHttpClient("FaceRecognition", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add Face Recognition Service (based on config)
var faceProvider = builder.Configuration["FaceRecognition:Provider"] ?? "Mock";
switch (faceProvider)
{
    case "HuggingFace":
        builder.Services.AddScoped<IFaceRecognitionService, HuggingFaceFaceRecognitionService>();
        break;
    case "Railway":
        builder.Services.AddScoped<IFaceRecognitionService, RailwayFaceRecognitionService>();
        break;
    case "FacePlusPlus":
        builder.Services.AddScoped<IFaceRecognitionService, FacePlusPlusFaceRecognitionService>();
        break;
    default:
        builder.Services.AddScoped<IFaceRecognitionService, MockFaceRecognitionService>();
        break;
}

builder.Services.AddScoped<IVotingService, VotingService>();
builder.Services.AddScoped<IResultsService, ResultsService>();

// Add Encryption
builder.Services.Configure<SecureVote.Encryption.EncryptionOptions>(
    builder.Configuration.GetSection("Encryption"));
builder.Services.AddScoped<SecureVote.Encryption.IEncryptionService, SecureVote.Encryption.EncryptionService>();

// Add Controllers
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Auto-create database schema and seed data on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

// Seed database (creates default admin if not exists)
await app.Services.SeedDatabaseAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();

Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
// Serve static files from wwwroot
app.UseStaticFiles();

// Serve uploaded files (candidate photos, voter photos)
var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "uploads")),
    RequestPath = "/uploads"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ===== Test Data Endpoints =====
app.MapGet("/seed-test-data", async (IServiceProvider sp) =>
{
    await TestDataSeeder.SeedTestDataAsync(sp);
    return Results.Ok(new
    {
        message = "✅ Test data seeded! Created 3 elections (Presidential + Parliamentary + University) with 500 voters and ~970 encrypted votes.",
        next_steps = new[]
        {
            "1. Login as test organizer: POST /api/auth/login { email: 'testorg@securevote.com', password: 'Test@123456' }",
            "2. Count presidential votes: POST /api/results/count/{electionId}",
            "3. Count parliamentary votes: POST /api/results/count/{electionId}",
            "4. Count university votes: POST /api/results/count/{electionId}",
            "5. View results: GET /api/results/{electionId}",
            "6. View by governorate: GET /api/results/{electionId}/by-governorate",
            "7. View participation: GET /api/results/{electionId}/participation",
            "8. Clean up: GET /clear-test-data"
        }
    });
});

app.MapGet("/clear-test-data", async (IServiceProvider sp) =>
{
    await TestDataSeeder.ClearTestDataAsync(sp);
    return Results.Ok(new { message = "✅ Test data cleared successfully!" });
});

app.Run();

