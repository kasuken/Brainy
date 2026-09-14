using System.Globalization;
using Brainy.Application.Interfaces.Services;
using Brainy.Application.Localization;

namespace Brainy.Web.Localization;

/// <summary>
/// Applies an authenticated user's stored UI-culture preference for this request, overriding
/// whatever RequestLocalizationMiddleware negotiated from the cookie/Accept-Language/default.
/// Must run after authentication (so <c>HttpContext.User</c> is populated) and before the Razor
/// Components render pipeline.
///
/// This has to be real middleware, not something done inside a component's lifecycle method
/// (e.g. OnInitializedAsync): setting <see cref="CultureInfo.CurrentCulture"/> only flows
/// forward through the SAME async call chain — everything the current method goes on to
/// directly await or call — the same way any <c>AsyncLocal&lt;T&gt;</c>-backed value does. A
/// middleware's own method directly awaits <c>next()</c>, so a mutation made here before
/// calling it is visible to the entire rest of the request, including component rendering. A
/// mutation made instead inside a component deep in the render tree is invisible to sibling
/// components rendered afterward, because the renderer's dispatch loop — not the component's
/// own method — is what goes on to render them, and it captured its ambient context before that
/// component's lifecycle method ran.
/// </summary>
internal static class UserCulturePreferenceMiddlewareExtensions
{
    public static IApplicationBuilder UseUserCulturePreference(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var cultureService = context.RequestServices.GetRequiredService<IUserCultureService>();
                var storedCultureId = await cultureService.GetCultureIdAsync(context.RequestAborted);
                if (string.IsNullOrWhiteSpace(storedCultureId))
                {
                    // Nothing recorded yet: persist whatever RequestLocalizationMiddleware just
                    // negotiated, so it stays stable across future sessions/devices instead of
                    // being renegotiated (and potentially drifting) on every request.
                    var negotiatedId = SupportedCultures.NormalizeToStorableId(CultureInfo.CurrentUICulture);
                    await cultureService.SetCultureIdAsync(negotiatedId, context.RequestAborted);
                }
                else if (SupportedCultures.IsSupported(storedCultureId))
                {
                    var culture = SupportedCultures.Resolve(storedCultureId);
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = culture;
                }
                // An unsupported stored id (e.g. a locale since removed) is left as whatever
                // RequestLocalizationMiddleware already negotiated.
            }

            await next();
        });
}
