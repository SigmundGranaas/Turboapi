namespace Turboapi.Application.UseCases.Commands.LoginUserWithPassword
{
    public record LoginUserWithPasswordCommand(
        string Email,
        string Password);
}