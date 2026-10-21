namespace BookStore.Domain.Exceptions;

public sealed class ConflictException(string message) : Exception(message);
