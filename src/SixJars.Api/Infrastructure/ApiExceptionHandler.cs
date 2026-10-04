using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Api.Infrastructure;

/// <summary>把 Application／Domain 的例外對應成 ProblemDetails；其他例外交給預設的 500 處理。</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException validation => new ValidationProblemDetails(validation.Errors
                .GroupBy(e => ToBodyFieldName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
            },
            DomainException domain => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = domain.Message,
                Extensions = { ["code"] = domain.Code },
            },
            NotFoundException notFound => new ProblemDetails { Status = StatusCodes.Status404NotFound, Detail = notFound.Message },
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Detail = "資料已被其他人修改，請重新載入後再試。",
            },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>
    /// 把 FluentValidation 的屬性路徑換成 body 欄位名稱：拿掉 command 包裝輸入用的 <c>Input.</c>，
    /// 每一段轉成 camelCase（例：<c>Input.CounterAccountId</c> → <c>counterAccountId</c>）。
    /// 新增與修改因此回同一套 key，前端可直接對應表單欄位。
    /// </summary>
    internal static string ToBodyFieldName(string propertyName)
    {
        const string inputPrefix = "Input.";
        var path = propertyName.StartsWith(inputPrefix, StringComparison.Ordinal) ? propertyName[inputPrefix.Length..] : propertyName;
        return string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }
}
