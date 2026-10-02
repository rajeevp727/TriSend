using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using TriSend.Auth.Application;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Protection;

namespace TriSend.Auth.Api;

public sealed class SessionRefreshTokenValidationHandler(ISessionService sessions)
    : IOpenIddictServerHandler<ValidateTokenContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseScopedHandler<SessionRefreshTokenValidationHandler>()
            .SetOrder(ValidateIdentityModelToken.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ValidateTokenContext context)
    {
        if (!context.ValidTokenTypes.Contains(TokenTypeIdentifiers.RefreshToken))
            return;

        var subject = context.Principal?.FindFirstValue(OpenIddictConstants.Claims.Subject);
        var session = context.Principal?.FindFirstValue("session_id");

        if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(session, out var sessionId))
        {
            context.Reject(
                error: OpenIddictConstants.Errors.InvalidGrant,
                description: "The refresh token is not associated with a valid TriSend session.");
            return;
        }

        if (!await sessions.IsActiveAsync(sessionId, userId, CancellationToken.None))
        {
            context.Reject(
                error: OpenIddictConstants.Errors.InvalidGrant,
                description: "The TriSend session is no longer active.");
        }
    }
}
