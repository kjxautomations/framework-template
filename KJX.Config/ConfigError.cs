namespace KJX.Config;

public class ConfigError(string message, Exception? baseException)
    : ApplicationException(message: message, baseException);