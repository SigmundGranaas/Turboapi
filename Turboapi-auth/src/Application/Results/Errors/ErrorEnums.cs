namespace Turboapi.Application.Results.Errors
{
    public enum GenericError
    {
        None = 0,
        Unknown,
        NotFound,
        InvalidInput
    }

    public enum RefreshTokenError
    {
        None = 0,
        InvalidToken = 1,
        Expired = 2,
        Revoked = 3,
        AccountNotFound = 4,
        StorageFailure = 5
    }

    public enum OAuthError
    {
        None = 0,
        ConfigurationError,
        NetworkError,
        TokenExchangeFailed,
        UserInfoFailed,
        InvalidCode,
        ProviderDeniedAccess,
        EmailNotVerified,
        InvalidState,
        MissingRequiredToken,
        TokenValidationError
    }

    public enum RegistrationError
    {
        None = 0,
        EmailAlreadyExists,
        WeakPassword,
        AccountCreationFailed,
        AuthMethodCreationFailed,
        TokenGenerationFailed,
        EventPublishFailed,
        InvalidInput
    }

    public enum LoginError
    {
        None = 0,
        InvalidCredentials,         
        AccountNotFound,            
        PasswordMethodNotFound,     
        AccountLocked,              
        AuthMethodVerificationFailed,
        TokenGenerationFailed,
        EventPublishFailed,
        InvalidInput
    }
}