using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Domain.Services;
using BookStore.Infrastructure.Events;
using BookStore.Infrastructure.Identity;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Persistence.Repositories;
using BookStore.Infrastructure.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

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

        services.AddDbContext<BookStoreDbContext>(options => options.UseBookStoreDatabase(connectionString));

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
        services.AddScoped<IRecommendationLogRepository, RecommendationLogRepository>();
        services.AddScoped<IModelVersionRepository, ModelVersionRepository>();

        services.AddOptions<EventQueueOptions>()
            .BindConfiguration(EventQueueOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ChannelInteractionQueue>();
        services.AddSingleton<IInteractionQueue>(sp => sp.GetRequiredService<ChannelInteractionQueue>());
        services.AddScoped<InteractionBatchStore>();
        services.AddHostedService<InteractionBatchWriter>();

        AddRecommendations(services, configuration);

        services.AddScoped<DatabaseInitializer>();

        return services;
    }

    private static void AddRecommendations(IServiceCollection services, IConfiguration configuration)
    {
        // Non-personalized collections: Redis shared by all API instances, or process memory without Redis.
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = "bookstore:";
            });
        }

        services.AddSingleton<IRecommendationCollectionCache, RecommendationCollectionCache>();

        var section = configuration.GetSection(RecommendationServiceOptions.SectionName);
        services.AddOptions<RecommendationServiceOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (section.Get<RecommendationServiceOptions>()?.BaseUrl is null)
        {
            // The baseline engine registered by the Services layer stays the primary engine.
            services.AddSingleton<IModelTrainingService, UnavailableTrainingService>();
            return;
        }

        // Starting a training job is not latency-critical: a plain timeout, no circuit breaker.
        services.AddHttpClient<IModelTrainingService, RecommendationTrainingClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<RecommendationServiceOptions>>().Value;
            http.BaseAddress = options.BaseUrl;
            http.DefaultRequestHeaders.Add(RecommendationServiceClient.ServiceKeyHeader, options.ServiceKey);
            http.Timeout = TimeSpan.FromSeconds(10);
        });

        services
            .AddHttpClient<RecommendationServiceClient>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<RecommendationServiceOptions>>().Value;
                http.BaseAddress = options.BaseUrl;
                http.DefaultRequestHeaders.Add(RecommendationServiceClient.ServiceKeyHeader, options.ServiceKey);
                http.Timeout = Timeout.InfiniteTimeSpan; // Enforced by the resilience pipeline.
            })
            .AddResilienceHandler("recommendation-service", (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<RecommendationServiceOptions>>().Value;

                // No retries: there is no time for them within 100 ms; the caller falls back instead.
                // The circuit breaker is outermost, so timeouts count as failures: while it is open,
                // requests go straight to the fallback instead of waiting for the timeout.
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = options.FailureRatio,
                    MinimumThroughput = options.MinimumThroughput,
                    SamplingDuration = options.SamplingDuration,
                    BreakDuration = options.BreakDuration,
                });
                pipeline.AddTimeout(options.Timeout);
            });

        services.Replace(ServiceDescriptor.Scoped<IRecommendationEngine>(
            sp => sp.GetRequiredService<RecommendationServiceClient>()));
    }
}
