using System.Security.Claims;

namespace Sistemas_para_pericias.Security;

public interface ICurrentUser
{
    int Id { get; }
}

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public int Id => int.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : 0;
}
