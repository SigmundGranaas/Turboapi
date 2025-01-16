namespace Turboapi_geo.domain.exception;

public class LocationNotFoundException: Exception
{
    public LocationNotFoundException(string? message) : base(message)
    {
    }
}