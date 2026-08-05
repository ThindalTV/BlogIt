using Microsoft.AspNetCore.Routing;

namespace BlogIt;

public interface IBlogItEndpointContributor
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
