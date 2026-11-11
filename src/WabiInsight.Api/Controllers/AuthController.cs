using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WabiInsight.Api.Authentication;
using WabiInsight.Api.Contracts;
using WabiInsight.Api.Security;
using WabiInsight.Core.Authentication;
using IAuthenticationService = WabiInsight.Core.Authentication.IAuthenticationService;
using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authentication;
    private readonly IAntiforgery _antiforgery;
    private readonly IClock _clock;

    public AuthController(IAuthenticationService authentication, IAntiforgery antiforgery, IClock clock)
    {
        _authentication = authentication;
        _antiforgery = antiforgery;
        _clock = clock;
    }

    /// <summary>Issues the antiforgery cookie/token pair. Called before login and again after login (tokens are bound to the identity).</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public ActionResult<CsrfTokenResponse> Csrf()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new CsrfTokenResponse(tokens.RequestToken!, tokens.HeaderName!));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        var result = await _authentication.LoginAsync(
            request.Email,
            request.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.Outcome != LoginOutcome.Success || result.Account is null)
        {
            var (code, detail) = result.Outcome switch
            {
                LoginOutcome.LockedOut => ("locked_out", "The account is temporarily locked after repeated failed attempts."),
                LoginOutcome.AccountDisabled => ("account_disabled", "The account is not active."),
                LoginOutcome.OrganizationSuspended => ("organization_suspended", "The organization is suspended."),
                _ => ("invalid_credentials", "The email or password is incorrect."),
            };

            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Login failed", detail: detail, type: $"https://wabi-insight.dev/problems/{code}");
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            WabiClaims.ToClaimsPrincipal(result.Account, _clock.UtcNow),
            CookieAuthenticationSetup.NewSessionProperties());

        return Ok(ToSession(result.Account));
    }

    [HttpPost("logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _authentication.RecordLogoutAsync(cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("me")]
    public ActionResult<SessionResponse> Me([FromServices] ITenantContext tenant)
    {
        var p = tenant.Principal;
        return Ok(new SessionResponse(
            (p.AdministratorId ?? p.UserId)!.Value,
            p.PrincipalType.ToString(),
            User.FindFirst(WabiClaims.Email)?.Value ?? string.Empty,
            p.DisplayName,
            p.OrganizationId,
            p.Roles.ToList(),
            WabiClaims.MustChangePasswordClaim(User),
            User.FindFirst(WabiClaims.Language)?.Value ?? "en"));
    }

    /// <summary>
    /// The signed-in person's own interface language (C10), kept on the account so it follows them to every browser and
    /// device. The session is re-issued with the new language and nothing else changed, so the next reload agrees.
    /// </summary>
    [HttpPut("language")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SessionResponse>> SetLanguage([FromBody] SetLanguageRequest request, CancellationToken cancellationToken)
    {
        var account = await _authentication.SetPreferredLanguageAsync(request.Language ?? string.Empty, cancellationToken);
        if (account is null)
        {
            ModelState.AddModelError(nameof(request.Language), "language_invalid");
            return ValidationProblem(ModelState);
        }

        // Only the language changes in the session. Its security stamp, the time that stamp was last checked and the
        // time the person signed in stay as they were, so a session that a password change or the absolute lifetime is
        // about to end still ends (review S1).
        var current = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            WabiClaims.WithLanguage(User, account.PreferredLanguage),
            current.Properties ?? CookieAuthenticationSetup.NewSessionProperties());
        return Ok(ToSession(account));
    }

    [HttpPost("change-password")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SessionResponse>> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authentication.ChangePasswordAsync(request.CurrentPassword, request.NewPassword, cancellationToken);

        if (result.Outcome != ChangePasswordOutcome.Success || result.Account is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(result.Outcome == ChangePasswordOutcome.CurrentPasswordInvalid ? nameof(request.CurrentPassword) : nameof(request.NewPassword), error);
            }

            return ValidationProblem(ModelState);
        }

        // Re-issue the session with the new security stamp so this client stays signed in; every other session is invalidated.
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            WabiClaims.ToClaimsPrincipal(result.Account, _clock.UtcNow),
            CookieAuthenticationSetup.NewSessionProperties());

        return Ok(ToSession(result.Account));
    }

    private static SessionResponse ToSession(AuthenticatedAccount account)
    {
        var p = account.Principal;
        return new SessionResponse(
            (p.AdministratorId ?? p.UserId)!.Value,
            p.PrincipalType.ToString(),
            account.Email,
            p.DisplayName,
            p.OrganizationId,
            p.Roles.ToList(),
            account.MustChangePassword,
            account.PreferredLanguage);
    }
}
