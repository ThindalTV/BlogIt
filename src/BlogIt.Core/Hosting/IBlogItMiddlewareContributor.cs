using Microsoft.AspNetCore.Builder;

namespace BlogIt;

public interface IBlogItMiddlewareContributor
{
    void Configure(IApplicationBuilder application);
}
