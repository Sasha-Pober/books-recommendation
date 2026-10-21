using BookStore.Domain.Services;
using BookStore.Services.Authors;
using BookStore.Services.Books;
using BookStore.Services.Genres;
using BookStore.Services.Interactions;
using BookStore.Services.Recommendations;
using BookStore.Services.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookStore.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<IBookService, BookService>();
        services.AddScoped<IAuthorService, AuthorService>();
        services.AddScoped<IGenreService, GenreService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IInteractionService, InteractionService>();
        services.AddScoped<IRecommendationService, RecommendationService>();

        services.AddScoped<BaselineRecommendationEngine>();
        // Default engine; Infrastructure can register the ML-service client instead.
        services.TryAddScoped<IRecommendationEngine>(sp => sp.GetRequiredService<BaselineRecommendationEngine>());

        return services;
    }
}
