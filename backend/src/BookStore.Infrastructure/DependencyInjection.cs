using BookStore.Domain.Repositories;
using BookStore.Infrastructure.Identity;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BookStore");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'BookStore' is not configured.");
        }

        services.AddDbContext<BookStoreDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention());

        // Identity stores only; authentication (JWT) is configured in the API.
        services
            .AddIdentityCore<AppUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<BookStoreDbContext>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BookStoreDbContext>());
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IAuthorRepository, AuthorRepository>();
        services.AddScoped<IGenreRepository, GenreRepository>();
        services.AddScoped<IPublisherRepository, PublisherRepository>();
        services.AddScoped<ISeriesRepository, SeriesRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRatingRepository, RatingRepository>();
        services.AddScoped<IInteractionRepository, InteractionRepository>();

        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
