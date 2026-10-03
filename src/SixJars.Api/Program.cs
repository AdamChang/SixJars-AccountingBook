using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using SixJars.Api.Endpoints;
using SixJars.Api.Infrastructure;
using SixJars.Application;
using SixJars.Application.Common;
using SixJars.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("SixJars")
    ?? throw new InvalidOperationException("未設定資料庫連線字串：請設定環境變數 ConnectionStrings__SixJars。");

builder.Services.AddSixJarsInfrastructure(connectionString);
builder.Services.AddSixJarsApplication(builder.Configuration["MediatR:LicenseKey"]);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// 暫時只有 cookie；T36 換成完整的 Google OIDC 設定。API 未登入時回 401，不轉址到登入頁。
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => o.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
var api = app.MapGroup("/api").RequireAuthorization();
api.MapBooksEndpoints();
app.Run();

public partial class Program;
