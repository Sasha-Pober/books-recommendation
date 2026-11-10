using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Infrastructure.Events;
using BookStore.Infrastructure.Identity;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Identity stores only; JWT validation is configured in the API.
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<BookStoreDbContext>();

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ITokenService, TokenService>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BookStoreDbContext>());
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IAuthorRepository, AuthorRepository>();
        services.AddScoped<IGenreRepository, GenreRepository>();
        services.AddScoped<IPublisherRepository, PublisherRepository>();
        services.AddScoped<ISeriesRepository, SeriesRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRatingRepository, RatingRepository>();
        services.AddScoped<IInteractionRepository, InteractionRepository>();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IWishlistRepository, WishlistRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddOptions<EventQueueOptions>()
            .BindConfiguration(EventQueueOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ChannelInteractionQueue>();
        services.AddSingleton<IInteractionQueue>(sp => sp.GetRequiredService<ChannelInteractionQueue>());
        services.AddScoped<InteractionBatchStore>();
        services.AddHostedService<InteractionBatchWriter>();

        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
