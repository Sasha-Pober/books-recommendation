using BookStore.Domain.Services;
using BookStore.Services.Auth;
using BookStore.Services.Authors;
using BookStore.Services.Books;
using BookStore.Services.Catalog;
using BookStore.Services.Genres;
using BookStore.Services.Interactions;
using BookStore.Services.Ratings;
using BookStore.Services.Recommendations;
using BookStore.Services.Shopping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookStore.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBookService, BookService>();
        services.AddScoped<IAuthorService, AuthorService>();
        services.AddScoped<IGenreService, GenreService>();
        services.AddScoped<IPublisherService, PublisherService>();
        services.AddScoped<ISeriesService, SeriesService>();
        services.AddScoped<IInteractionService, InteractionService>();
        services.AddScoped<IRatingService, RatingService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IRecommendationService, RecommendationService>();

        services.AddScoped<BaselineRecommendationEngine>();
        // Default engine; Infrastructure can register the ML-service client instead.
        services.TryAddScoped<IRecommendationEngine>(sp => sp.GetRequiredService<BaselineRecommendationEngine>());

        return services;
    }
}
