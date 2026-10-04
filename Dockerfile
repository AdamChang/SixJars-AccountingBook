# SixJars API（spec §8.4）：multi-stage build，執行階段只有 ASP.NET Core runtime 與發佈成果，以非 root 使用者執行。
# 建置：docker build -t sixjars-api .
# 設定一律由環境變數提供（見 .env.example）；image 裡沒有任何連線字串或金鑰。

# ---- 建置階段 ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先只複製專案檔並 restore：原始碼改動時，這一層的 NuGet 快取仍可重用。
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SixJars.Domain/SixJars.Domain.csproj src/SixJars.Domain/
COPY src/SixJars.Application/SixJars.Application.csproj src/SixJars.Application/
COPY src/SixJars.Infrastructure/SixJars.Infrastructure.csproj src/SixJars.Infrastructure/
COPY src/SixJars.Api/SixJars.Api.csproj src/SixJars.Api/
RUN dotnet restore src/SixJars.Api/SixJars.Api.csproj

# 只複製 src/：tests、docs、reference/（個資）都不進 build context（見 .dockerignore）。
COPY src/ src/
RUN dotnet publish src/SixJars.Api/SixJars.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# ---- 執行階段 ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Cloud Run 預設注入 PORT=8080；這裡固定監聽 8080，部署時不要另外改 --port。
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# 非 root：APP_UID 是 .NET 官方 image 內建的 app 使用者。
USER $APP_UID
ENTRYPOINT ["dotnet", "SixJars.Api.dll"]
