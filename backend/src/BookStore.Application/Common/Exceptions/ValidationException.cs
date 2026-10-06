namespace BookStore.Application.Common.Exceptions;

/// <summary>Business-rule validation failure (input shape is validated at the API boundary).</summary>
public sealed class ValidationException(string message) : Exception(message);
