using Microsoft.Extensions.DependencyInjection;

namespace MetaAnime.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Mediator handlers with Scoped lifetime.
    /// Must be called from this project (MetaAnime.Application) so that the
    /// Mediator.SourceGenerator sees the AddMediator call at compile time and
    /// generates handler registrations with the correct lifetime.
    /// </summary>
    public static IServiceCollection AddApplicationMediator(this IServiceCollection services)
    {
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        return services;
    }
}
