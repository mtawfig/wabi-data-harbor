using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WabiInsight.Api.Authentication;
using WabiInsight.Api.Authorization;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Api.Security;

/// <summary>
/// Business users never receive technical fields (F5): the AI provider, model, token counts and cost are removed from
/// answers, plans and history before the response is written, unless the caller is an Organization Administrator. The
/// values are still recorded on the server (usage events, analysis metrics) for the AI usage and insights pages.
/// </summary>
public sealed class TechnicalFieldsFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (context.Result is ObjectResult { Value: { } value } result && !SeesTechnicalFields(context.HttpContext.User))
        {
            result.Value = Reduce(value);
        }

        return next();
    }

    /// <summary>Organization Administrators see technical fields; every other organization user does not.</summary>
    public static bool SeesTechnicalFields(System.Security.Claims.ClaimsPrincipal user)
        => user.HasClaim(WabiClaims.Role, Policies.OrganizationAdminRole);

    internal static object Reduce(object value) => value switch
    {
        AskResponse answer => answer.WithoutTechnicalFields(),
        PlanResponse plan => new PlanView(plan.Plan, plan.Authorized, plan.Classification),
        HistoryDetailDto detail => detail.WithoutTechnicalFields(),
        HistorySummaryDto summary => summary.WithoutTechnicalFields(),
        PagedResult<HistorySummaryDto> page => page with { Items = page.Items.Select(i => i.WithoutTechnicalFields()).ToList() },
        _ => value,
    };

    /// <summary>A validated plan without the provider usage.</summary>
    public sealed record PlanView(AnalysisPlan Plan, IReadOnlyList<AuthorizedPlan> Authorized, Core.Entities.Metadata.DataClassification Classification);
}
