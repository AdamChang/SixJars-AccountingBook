using MediatR;
using SixJars.Application.Members;

namespace SixJars.Api.Endpoints;

internal static class MeEndpoints
{
    /// <summary>目前登入的使用者與他所屬的帳本；前端登入後以此決定要開哪一本帳。</summary>
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", (ISender sender, CancellationToken ct) => sender.Send(new GetMe(), ct));
        return api;
    }
}
