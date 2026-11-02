namespace AWYKEN.Shared.Configuration;

/// <summary>
/// Raised when the Azure SQL Database keys are missing or malformed. The message
/// explains which keys and which <c>.env</c> file the app looked for.
/// </summary>
public sealed class AwykenDatabaseConfigurationException : InvalidOperationException
{
    public AwykenDatabaseConfigurationException(string message) : base(message)
    {
    }

    public AwykenDatabaseConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
