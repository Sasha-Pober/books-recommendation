using System.ComponentModel.DataAnnotations;

namespace BookStore.Presentation.Users;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record CreateUserRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(100)] string DisplayName);
