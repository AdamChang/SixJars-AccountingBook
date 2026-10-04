using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Auditing;
using SixJars.Application.Common;

namespace SixJars.Application;

public static class DependencyInjection
{
    /// <summary>
    /// 註冊 MediatR（含 <see cref="BookAccessBehavior{TRequest, TResponse}"/> 與 <see cref="ValidationBehavior{TRequest, TResponse}"/>）、
    /// 本組件所有 validator 與 <see cref="IAuditTrail"/>。
    /// </summary>
    public static IServiceCollection AddSixJarsApplication(this IServiceCollection services, string? mediatRLicenseKey)
    {
        services.AddMediatR(cfg =>
        {
            // 沒有 key 時 MediatR 只記一筆 warning，開發與測試不受影響（spike S1）；正式環境由 Secret Manager 提供 key。
            cfg.LicenseKey = mediatRLicenseKey;
            cfg.RegisterServicesFromAssemblyContaining<ISixJarsDbContext>();
            // 依註冊順序由外而內執行：先授權、再驗證，非成員一律 404，不會因為內容不合法而拿到 400。
            cfg.AddOpenBehavior(typeof(BookAccessBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssemblyContaining<ISixJarsDbContext>(includeInternalTypes: true);
        // 依賴 ISixJarsDbContext（scoped）、ICurrentUser 與 TimeProvider，由 composition root 提供。
        services.AddScoped<IAuditTrail, AuditTrail>();
        return services;
    }
}
