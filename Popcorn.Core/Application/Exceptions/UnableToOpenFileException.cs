namespace Popcorn.Application.Exceptions;

internal class UnableToOpenFileException(string message, Exception ex) : Exception(message, ex);