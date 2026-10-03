using SixJars.Api.Endpoints;
using SixJars.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("SixJars")
    ?? throw new InvalidOperationException("未設定資料庫連線字串：請設定環境變數 ConnectionStrings__SixJars。");

builder.Services.AddSixJarsInfrastructure(connectionString);
builder.Services.AddProblemDetails();

var app = builder.Build();
app.MapHealthEndpoints();
app.Run();

public partial class Program;
