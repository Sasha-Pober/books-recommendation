using BookStore.Application.Abstractions;
using BookStore.Application.Authors;
using BookStore.Application.Books;
using BookStore.Application.Genres;
using BookStore.Application.Interactions;
using BookStore.Application.Recommendations;
using BookStore.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookStore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
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
