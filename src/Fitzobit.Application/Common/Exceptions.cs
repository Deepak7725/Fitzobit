namespace Fitzobit.Application.Common;

public class NotFoundException(string message) : Exception(message);

/// <summary>A request that is well-formed but breaks a business rule.</summary>
public class BusinessRuleException(string message) : Exception(message);
