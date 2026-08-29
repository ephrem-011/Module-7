using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using Scalar.AspNetCore;
using Microsoft.EntityFrameworkCore;
using TmsApi.Infrastructure.Persistence;
using TmsApi.Domain.Entities;
using TmsApi.Infrastructure.Services;
using TmsApi.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using TmsApi.Application.Filters;
using Microsoft.AspNetCore.Identity;
using Asp.Versioning;
using TmsApi.Api.Middlewares;
using TmsApi.Api.Hubs;
using TmsApi.Infrastructure.SeedData;
using TmsApi.Application.Behaviors;
using TmsApi.Api.ExceptionHandlers;
using TmsApi.Application.Enrollments.Commands;
using Microsoft.Extensions.Caching.Hybrid;
using MediatR;
using FluentValidation;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TmsApi.Api.RateLimiting;
using TmsApi.Infrastructure.Transcripts;
using System.Threading.Channels;
using TmsApi.Application.Transcripts;
using TmsApi.Infrastructure.Workers;
using TmsApi.Application.Notifications;
using TmsApi.Api.Notifications;
using FluentValidation.Validators;
using Microsoft.AspNetCore.Antiforgery;
using TmsApi.Infrastructure.Identity;


var builder = WebApplication.CreateBuilder(args);


builder.Services.AddRateLimiter(options =>

{
options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext,string>(httpContext =>
{
var (partitionKey, tier) = ApiKeyResolver.Resolve(httpContext);return tier switch
{
ApiKeyTier.Paid => RateLimitPartition.GetTokenBucketLimiter(
partitionKey: $"paid:{partitionKey}",
factory: _ => new TokenBucketRateLimiterOptions
{
TokenLimit = 200,
TokensPerPeriod = 100,
ReplenishmentPeriod = TimeSpan.FromSeconds(10),
QueueLimit = 0,
AutoReplenishment = true
}),
ApiKeyTier.Free => RateLimitPartition.GetTokenBucketLimiter(
partitionKey: $"free:{partitionKey}",
factory: _ => new TokenBucketRateLimiterOptions
{
TokenLimit = 30,
TokensPerPeriod = 10,
ReplenishmentPeriod = TimeSpan.FromSeconds(10),
QueueLimit = 0,
AutoReplenishment = true
}),
_ => RateLimitPartition.GetTokenBucketLimiter(
partitionKey: $"anon:{partitionKey}",
factory: _ => new TokenBucketRateLimiterOptions
{
TokenLimit = 10,
TokensPerPeriod = 5,
ReplenishmentPeriod = TimeSpan.FromSeconds(10),
QueueLimit = 0,
AutoReplenishment = true
})
};
});
options.AddTokenBucketLimiter("search", opt =>
{
    opt.TokenLimit = 10;
    opt.TokensPerPeriod = 5;
    opt.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
    opt.QueueLimit = 2;
    opt.AutoReplenishment = true;
});
options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;options.OnRejected = async (context, ct) =>
{
var retryAfter = "10";
if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ts))
retryAfter = ((int)ts.TotalSeconds).ToString();
context.HttpContext.Response.Headers.RetryAfter = retryAfter;context.HttpContext.Response.ContentType = "application/problem+json";
await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
{
Title = "Rate limit exceeded",
Detail = $"Too many requests. Retry after {retryAfter} seconds.",
Status = StatusCodes.Status429TooManyRequests,
Type = "https://tms.local/errors/rate_limit_exceeded"
}, ct);
};
});
builder.Services.AddControllers(options =>
{
    options.Filters.Add<AuditLogFilter>();
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<
    ITranscriptNotificationService,
    SignalRTranscriptNotificationService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddHybridCache(options =>
{
options.DefaultEntryOptions = new HybridCacheEntryOptions
{
Expiration = TimeSpan.FromMinutes(10),
LocalCacheExpiration = TimeSpan.FromMinutes(2)
};
});
builder.Services.AddSingleton(
    Channel.CreateBounded<TranscriptRequest>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        }));
builder.Services.AddSingleton<
    ITranscriptStatusStore,
    InMemoryTranscriptStatusStore>();
builder.Services.AddScoped<ICachedCourseService, CachedCourseService>();
builder.Services.AddHostedService<TranscriptWorker>();
builder.Services.AddOpenApi("v1", options =>
{
options.ShouldInclude = description =>
description.GroupName == "v1";
});
builder.Services.AddOpenApi("v2", options =>
{
options.ShouldInclude = description =>
description.GroupName == "v2";
});
builder.Services.AddApiVersioning(options =>
{
options.DefaultApiVersion = new ApiVersion(1, 0);
options.AssumeDefaultVersionWhenUnspecified = true;
options.ReportApiVersions = true;
options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
options.GroupNameFormat = "'v'VVV";
options.SubstituteApiVersionInUrl = true;
});
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
// ... GlobalLimiter from Step 2 stays as-is ...
options.AddConcurrencyLimiter("transcripts", opt =>
{
opt.PermitLimit = 5; // 5 in-flight transcripts maximum
opt.QueueLimit = 20; // queue up to 20 more
opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;});
});

builder.Services.AddOpenApi();
builder.Services.AddDbContext<TmsDbContext>(options =>
options.UseNpgsql(builder.Configuration.GetConnectionString("TmsDatabase")));

builder.Services.AddDbContext<TmsDbContext>(options =>
options.UseNpgsql(builder.Configuration.GetConnectionString("TmsDatabase"))
.LogTo(Console.WriteLine, LogLevel.Information) // Log SQLto output window
.EnableSensitiveDataLogging()); // Show parameters in querylogs (dev only)
builder.Services
    .AddAuthentication("Training")
    .AddScheme<AuthenticationSchemeOptions, TrainingAuthHandler>("Training", null);
builder.Services.AddAuthorization();

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddValidatorsFromAssembly(typeof(EnrollStudentValidator).Assembly);
// LoggingBehavior FIRST—it must wrap ValidationBehavior
builder.Services.AddTransient(typeof(IPipelineBehavior<,>),typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>),typeof(ValidationBehavior<,>));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddProblemDetails();

builder.Services.AddScoped<ICourseService, CourseService>();

builder.Services.AddScoped<IEnrollmentServices, EnrollmentService>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
});
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("TmsClient", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
});

builder.Services
    .AddIdentityCore<TmsUser>(options =>
    {
        // Password policy
        options.Password.RequiredLength = 12;
        options.Password.RequireUppercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;

        // Lockout protection
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<TmsDbContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Development tools only
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
{
options.WithTitle("TMS API Reference")
.WithTheme(ScalarTheme.DeepSpace)
.WithDefaultHttpClient(ScalarTarget.CSharp,
ScalarClient.HttpClient)
// Tell Scalar to pull both documents into its sidebar dropdownoptions
.AddDocument("v1", "API Version 1.0")
.AddDocument("v2", "API Version 2.0");
});
}
else
{
    // Production safety
    app.UseExceptionHandler();
}
app.UseStatusCodePages();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler("/error");
app.UseHttpsRedirection();

app.UseRouting();

app.UseRateLimiter();
app.MapHealthChecks("/health/live").DisableRateLimiting();
app.MapHealthChecks("/health/ready").DisableRateLimiting();
app.MapHub<EnrollmentHub>("/hubs/enrollment");
app.MapHub<TmsHub>("/hubs/tms");
app.UseCors("TmsClient");
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true ||
        context.Request.Cookies.ContainsKey("tms_auth"))
    {
        var antiforgery =
            context.RequestServices.GetRequiredService<IAntiforgery>();

        var tokens = antiforgery.GetAndStoreTokens(context);

        context.Response.Cookies.Append(
            "XSRF-TOKEN",
            tokens.RequestToken!,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = !app.Environment.IsDevelopment(),
                SameSite = SameSiteMode.Strict
            });
    }

    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<V1DeprecationMiddleware>();
app.MapControllers();

//Seed test data at startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();

    // Applies any pending migrations; keeps migration history intact
    context.Database.Migrate();

    if (!context.Students.Any())
    {
            var students = new List<Student>
    {
        new() { RegistrationNumber = "TMS-2026-0001", Name = "Alice Smith", GPA = 3.8m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0002", Name = "Bob Jones", GPA = 2.9m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0003", Name = "Charlie Brown", GPA = 3.4m, IsActive = false },
        new() { RegistrationNumber = "TMS-2026-0004", Name = "Diana Prince", GPA = 3.9m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0005", Name = "Evan Wright", GPA = 2.5m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0006", Name = "Alex Johnson", GPA = 3.1m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0007", Name = "Sophia Lee", GPA = 3.6m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0008", Name = "Daniel Kim", GPA = 2.7m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0009", Name = "Emma Davis", GPA = 3.2m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0010", Name = "Michael Scott", GPA = 3.0m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0011", Name = "Jim Halpert", GPA = 3.5m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0012", Name = "Pam Beesly", GPA = 3.7m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0013", Name = "Dwight Schrute", GPA = 2.8m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0014", Name = "Ryan Howard", GPA = 2.6m, IsActive = false },
        new() { RegistrationNumber = "TMS-2026-0015", Name = "Andy Bernard", GPA = 3.3m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0016", Name = "Stanley Hudson", GPA = 2.9m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0017", Name = "Kevin Malone", GPA = 2.4m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0018", Name = "Oscar Martinez", GPA = 3.8m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0019", Name = "Angela Martin", GPA = 3.9m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0020", Name = "Kelly Kapoor", GPA = 3.1m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0021", Name = "Ryan Reynolds", GPA = 3.6m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0022", Name = "Chris Evans", GPA = 3.7m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0023", Name = "Tom Holland", GPA = 3.5m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0024", Name = "Zendaya Coleman", GPA = 3.9m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0025", Name = "Robert Downey", GPA = 3.8m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0026", Name = "Tony Stark", GPA = 4.0m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0027", Name = "Steve Rogers", GPA = 3.9m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0028", Name = "Bruce Banner", GPA = 3.7m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0029", Name = "Thor Odinson", GPA = 3.6m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0030", Name = "Natasha Romanoff", GPA = 3.8m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0031", Name = "Peter Parker", GPA = 3.5m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0032", Name = "Miles Morales", GPA = 3.6m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0033", Name = "Clark Kent", GPA = 3.9m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0034", Name = "Bruce Wayne", GPA = 3.4m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0035", Name = "Barry Allen", GPA = 3.3m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0036", Name = "Arthur Curry", GPA = 3.2m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0037", Name = "Diana Wayne", GPA = 3.8m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0038", Name = "Hal Jordan", GPA = 3.1m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0039", Name = "Oliver Queen", GPA = 3.0m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0040", Name = "Kara Zor-El", GPA = 3.9m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0041", Name = "John Snow", GPA = 3.2m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0042", Name = "Arya Stark", GPA = 3.6m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0043", Name = "Tyrion Lannister", GPA = 3.7m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0044", Name = "Daenerys Targaryen", GPA = 3.9m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0045", Name = "Jonas Kahnwald", GPA = 3.4m, IsActive = true },

        new() { RegistrationNumber = "TMS-2026-0046", Name = "Walter White", GPA = 3.8m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0047", Name = "Jesse Pinkman", GPA = 3.2m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0048", Name = "Saul Goodman", GPA = 3.5m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0049", Name = "Dexter Morgan", GPA = 3.7m, IsActive = true },
        new() { RegistrationNumber = "TMS-2026-0050", Name = "Sherlock Holmes", GPA = 4.0m, IsActive = true }
    };

    context.Students.AddRange(students);
    context.SaveChanges();

        var courses = new List<Course>
        {
            new()
            {
                Code = "CS-101",
                Title = "Introduction to Computer Science",
                MaxCapacity = 30
            },
            new()
            {
                Code = "CS-201",
                Title = "Data Structures and Algorithms",
                MaxCapacity = 25
            },
            new()
            {
                Code = "MAT-101",
                Title = "Calculus I",
                MaxCapacity = 40
            }
        };

        context.Courses.AddRange(courses);
        context.SaveChanges();

        var enrollments = new List<Enrollment>
        {
            new()
            {
                StudentId = students[0].Id,
                CourseId = courses[0].Id,
                Grade = 4.0m
            },
            new()
            {
                StudentId = students[0].Id,
                CourseId = courses[1].Id,
                Grade = 3.6m
            },
            new()
            {
                StudentId = students[1].Id,
                CourseId = courses[0].Id,
                Grade = 2.8m
            },
            new()
            {
                StudentId = students[3].Id,
                CourseId = courses[1].Id,
                Grade = 3.9m
            }
        };

        context.Enrollments.AddRange(enrollments);
        context.SaveChanges();
    }
}

//Seed courses table
if (app.Environment.IsDevelopment())
{
using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
await DataSeeder.SeedAsync(context);
}

app.Run();