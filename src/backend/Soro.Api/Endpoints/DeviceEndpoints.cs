using Soro.Api.Contracts;
using Soro.Application.Devices;
using Soro.Domain.Common;

namespace Soro.Api.Endpoints;

internal static class DeviceEndpoints
{
    /// <summary>Enrôlement : public (l'appareil n'a pas encore de jeton), donc derrière le plafond de débit par adresse.</summary>
    public static IEndpointRouteBuilder MapDeviceEnrollment(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/devices").AllowAnonymous().RequireRateLimiting(RateLimitingSetup.PublicPolicy)
            .MapPost("/enroll", EnrollAsync);
        return app;
    }

    /// <summary>Invitations, liste et révocation : réservées au propriétaire et au gérant (contrôlé dans les cas d'usage).</summary>
    public static IEndpointRouteBuilder MapDeviceEndpoints(this RouteGroupBuilder api)
    {
        var devices = api.MapGroup("/devices");
        devices.MapGet("", ListAsync);
        devices.MapPost("/invitations", InviteAsync);
        devices.MapGet("/invitations", ListInvitationsAsync);
        devices.MapPost("/invitations/{invitationId:guid}/revoke", RevokeInvitationAsync);
        devices.MapPost("/{deviceId:guid}/revoke", RevokeAsync);
        return api;
    }

    private static async Task<IResult> EnrollAsync(EnrollDeviceRequest request, EnrollDeviceHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new EnrollDeviceCommand(request.Token, request.DeviceName), cancellationToken);
        return result.ToHttp(done => Results.Ok(new DeviceEnrolledResponse(
            done.Device.Value, done.Secret, done.Tenant.Value, done.CommerceName, done.DeviceName)));
    }

    private static async Task<IResult> InviteAsync(
        CreateDeviceInvitationRequest request,
        CreateDeviceInvitationHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateDeviceInvitationCommand(request.SuggestedName), cancellationToken);
        return result.ToHttp(created => Results.Ok(new DeviceInvitationResponse(created.Id.Value, created.Token, created.ExpiresAt)));
    }

    private static async Task<IResult> ListAsync(ListDevicesHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(cancellationToken)).ToHttp(devices => Results.Ok(devices));

    private static async Task<IResult> RevokeAsync(Guid deviceId, RevokeDeviceHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new RevokeDeviceCommand(new DeviceId(deviceId)), cancellationToken)).ToHttp(_ => Results.NoContent());

    private static async Task<IResult> ListInvitationsAsync(ListDeviceInvitationsHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(cancellationToken)).ToHttp(pending => Results.Ok(pending));

    private static async Task<IResult> RevokeInvitationAsync(
        Guid invitationId,
        RevokeDeviceInvitationHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new RevokeDeviceInvitationCommand(new DeviceInvitationId(invitationId)), cancellationToken))
        .ToHttp(_ => Results.NoContent());
}
