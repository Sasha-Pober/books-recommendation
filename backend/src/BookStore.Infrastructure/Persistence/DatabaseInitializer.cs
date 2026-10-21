using BookStore.Domain.Common;
using BookStore.Domain.Entities;
using BookStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Persistence;

/// <summary>Applies migrations and seeds roles, demo accounts and a demo catalog. Intended for development.</summary>
public sealed class DatabaseInitializer(
    BookStoreDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>Demo accounts (email, password, role). Development only.</summary>
    private static readonly (string Email, string FullName, string Password, string Role)[] DemoUsers =
    [
        ("admin@bookstore.local", "Адміністратор", "Admin123!", Roles.Admin),
        ("manager@bookstore.local", "Менеджер", "Manager123!", Roles.Manager),
        ("customer@bookstore.local", "Покупець", "Customer123!", Roles.Customer),
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        await SeedRolesAsync();
        await SeedUsersAsync();

        if (!await db.Books.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Seeding demo catalog");
            SeedCatalog();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new AppRole { Name = role }), $"create role {role}");
            }
        }
    }

    private async Task SeedUsersAsync()
    {
        foreach (var (email, fullName, password, role) in DemoUsers)
        {
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = fullName };
            EnsureSucceeded(await userManager.CreateAsync(user, password), $"create user {email}");
            EnsureSucceeded(await userManager.AddToRoleAsync(user, role), $"add {email} to {role}");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to {action}: {errors}");
        }
    }

    private void SeedCatalog()
    {
        var fiction = new Genre { Name = "Художня література" };
        var fantastic = new Genre { Name = "Фантастика", Parent = fiction };
        var genres = new Dictionary<string, Genre>
        {
            ["Художня література"] = fiction,
            ["Фантастика"] = fantastic,
            ["Фентезі"] = new() { Name = "Фентезі", Parent = fantastic },
            ["Наукова фантастика"] = new() { Name = "Наукова фантастика", Parent = fantastic },
            ["Класика"] = new() { Name = "Класика", Parent = fiction },
            ["Сучасна проза"] = new() { Name = "Сучасна проза", Parent = fiction },
            ["Історичний роман"] = new() { Name = "Історичний роман", Parent = fiction },
            ["Пригоди"] = new() { Name = "Пригоди", Parent = fiction },
            ["Поезія"] = new() { Name = "Поезія", Parent = fiction },
            ["Драма"] = new() { Name = "Драма", Parent = fiction },
            ["Гумор"] = new() { Name = "Гумор", Parent = fiction },
        };

        var authors = new[]
            {
                "Тарас Шевченко", "Іван Франко", "Леся Українка", "Іван Багряний", "Валер'ян Підмогильний",
                "Сергій Жадан", "Софія Андрухович", "Марія Матіос", "Джон Р. Р. Толкін", "Френк Герберт",
                "Террі Пратчетт", "Ніл Ґейман",
            }
            .ToDictionary(name => name, name => new Author { FullName = name });

        var publishers = new[]
            {
                "Фоліо", "Meridian Czernowitz", "Видавництво Старого Лева", "А-ба-ба-га-ла-ма-га", "Астролябія", "КСД",
            }
            .ToDictionary(name => name, name => new Publisher { Name = name });

        // Demo data: Year is the year of first publication; publishers are illustrative editions.
        var lordOfTheRings = new Series { Name = "Володар перснів" };
        var duneChronicles = new Series { Name = "Хроніки Дюни" };

        Book NewBook(
            string title, string[] authorNames, string? publisher, int year, decimal price,
            string[] genreNames, string annotation, Series? series = null) => new()
        {
            Title = title,
            Annotation = annotation,
            Authors = authorNames.Select(a => authors[a]).ToList(),
            Publisher = publisher is null ? null : publishers[publisher],
            Series = series,
            Year = year,
            Language = "uk",
            Price = price,
            Stock = 10,
            Genres = genreNames.Select(g => genres[g]).ToList(),
        };

        db.Books.AddRange(
            NewBook("Кобзар", ["Тарас Шевченко"], "Фоліо", 1840, 250m, ["Поезія", "Класика"],
                "Збірка поезій Тараса Шевченка, що стала символом української літератури."),
            NewBook("Захар Беркут", ["Іван Франко"], "Фоліо", 1883, 180m, ["Історичний роман", "Класика"],
                "Історична повість про боротьбу громади Тухольщини проти монгольської навали у XIII столітті."),
            NewBook("Лісова пісня", ["Леся Українка"], "Фоліо", 1911, 150m, ["Драма", "Класика"],
                "Драма-феєрія про кохання лісової мавки Мавки та сільського хлопця Лукаша."),
            NewBook("Тигролови", ["Іван Багряний"], "Фоліо", 1944, 210m, ["Пригоди", "Класика"],
                "Роман про Григорія Многогрішного, засланого на Далекий Схід, і родину мисливців на тигрів."),
            NewBook("Місто", ["Валер'ян Підмогильний"], "Фоліо", 1928, 190m, ["Класика"],
                "Роман про сільського юнака Степана Радченка, який підкорює Київ 1920-х років."),
            NewBook("Інтернат", ["Сергій Жадан"], "Meridian Czernowitz", 2017, 320m, ["Сучасна проза"],
                "Роман про вчителя Пашу, який під час війни на Донбасі вирушає забрати племінника з інтернату."),
            NewBook("Ворошиловград", ["Сергій Жадан"], null, 2010, 300m, ["Сучасна проза"],
                "Роман про Германа, який повертається до рідного містечка на сході України, щоб урятувати бензоколонку брата."),
            NewBook("Felix Austria", ["Софія Андрухович"], "Видавництво Старого Лева", 2014, 280m,
                ["Сучасна проза", "Історичний роман"],
                "Роман про життя служниці Стефи в Станиславові на початку XX століття, за часів Австро-Угорщини."),
            NewBook("Солодка Даруся", ["Марія Матіос"], "А-ба-ба-га-ла-ма-га", 2004, 260m, ["Сучасна проза"],
                "Роман про трагічну долю буковинської жінки Дарусі, яку односельці вважають божевільною."),
            NewBook("Гобіт, або Туди і звідти", ["Джон Р. Р. Толкін"], "Астролябія", 1937, 350m, ["Фентезі", "Пригоди"],
                "Повість про гобіта Більбо Беґінса, який вирушає з ґномами по скарб, що його охороняє дракон Смауґ."),
            NewBook("Братство Персня", ["Джон Р. Р. Толкін"], "Астролябія", 1954, 420m, ["Фентезі", "Пригоди"],
                "Перша частина епопеї про Фродо та його друзів, які вирушають знищити Перстень Всевладдя.",
                lordOfTheRings),
            NewBook("Дюна", ["Френк Герберт"], "КСД", 1965, 380m, ["Наукова фантастика"],
                "Роман про Пола Атріда та боротьбу за пустельну планету Арракіс — єдине джерело прянощів.",
                duneChronicles),
            NewBook("Месія Дюни", ["Френк Герберт"], "КСД", 1969, 340m, ["Наукова фантастика"],
                "Продовження «Дюни»: Пол Атрід став імператором, але змова загрожує його владі.",
                duneChronicles),
            NewBook("Добрі передвісники", ["Террі Пратчетт", "Ніл Ґейман"], null, 1990, 330m, ["Фентезі", "Гумор"],
                "Комедійний роман про ангела й демона, які намагаються відвернути кінець світу."));
    }
}
