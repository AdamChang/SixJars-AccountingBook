using System.Text.Json.Serialization;
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
// 「今天」由注入的時鐘提供（/summary 省略 asOf 時使用），測試以 ConfigureTestServices 換成固定時間。
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// cookie＋Google OIDC，白名單即帳本成員表（ADR 0005）。API 未登入時回 401，不轉址到 Google。
builder.Services.AddSixJarsAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization();
// 非 GET 的請求一律驗證 XSRF token（ADR 0005），由 AntiforgeryFilter 掛在 /api 與 /auth/logout。
builder.Services.AddAntiforgery(AntiforgeryFilter.ConfigureOptions);
builder.Services.Configure<ForwardedHeadersOptions>(ForwardedHeadersSetup.Configure);

var app = builder.Build();
// 必須在 authentication 之前：OIDC 的 redirect_uri 與 Secure cookie 都依賴 Request.IsHttps。
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSpaHosting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<AntiforgeryFilter>();
api.MapAntiforgeryEndpoints().MapMeEndpoints();
api.MapBooksEndpoints().MapTransactionsEndpoints().MapPlannedExpensesEndpoints().MapRecurringPlannedExpensesEndpoints().MapBudgetsEndpoints()
    .MapSummaryEndpoints().MapAuditEndpoints()
    .MapExportsEndpoints();
app.Run();

public partial class Program;
