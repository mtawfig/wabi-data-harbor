using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;

namespace WabiInsight.Core.Monitoring;

/// <summary>A field a data template needs, satisfied by any approved, permitted column with one of the roles on the same object.</summary>
public sealed record TemplateSlot(string Slot, IReadOnlyList<ColumnSemanticRole> Roles);

/// <summary>
/// A ready-made rule (specification §35.1). Data templates declare the field roles they need and are offered only
/// when the organization's metadata satisfies them; access templates read the ledger and are always available
/// (some need Restricted columns to exist). Labels are localized in the web app by <see cref="Code"/>.
/// </summary>
public sealed record RuleTemplate(
    string Code,
    MonitoringRuleKind Kind,
    AlertSeverity Severity,
    IReadOnlyList<TemplateSlot> Slots,
    RuleDefinition Defaults,
    JobRecurrenceKind RecurrenceKind,
    TimeOnly TimeOfDay,
    int IntervalMinutes,
    bool RequiresRestrictedColumns = false);

public static class RuleTemplates
{
    public const string MultiDeviceSameAccount = "multi_device_same_account";
    public const string ManyEventsPerEntity = "many_events_per_entity";
    public const string AmountAboveThreshold = "amount_above_threshold";
    public const string MeasureChangeVersusPreviousPeriod = "change_vs_previous_period";
    public const string RestrictedFilteredAccess = "restricted_filtered_access";
    public const string RepeatedUnauthorizedAttempts = "repeated_unauthorized_attempts";
    public const string ExportsAboveBaseline = "exports_above_baseline";
    public const string QueriesAboveBaseline = "queries_above_baseline";
    public const string SensitiveOutsideWorkingHours = "sensitive_outside_working_hours";
    public const string NewUserSensitiveAccess = "new_user_sensitive_access";

    private static readonly ColumnSemanticRole[] EntityRoles = [ColumnSemanticRole.AccountIdentifier, ColumnSemanticRole.PersonIdentifier, ColumnSemanticRole.Identifier];

    public static readonly IReadOnlyList<RuleTemplate> All =
    [
        // The owner's example: the same account used from more than one device within a day.
        new(MultiDeviceSameAccount, MonitoringRuleKind.Data, AlertSeverity.High,
            [new("account", [ColumnSemanticRole.AccountIdentifier, ColumnSemanticRole.PersonIdentifier]), new("device", [ColumnSemanticRole.DeviceIdentifier]), new("time", [ColumnSemanticRole.Timestamp])],
            new RuleDefinition { Data = new DataRuleDefinition { WindowHours = 24, Measure = new RuleMeasure { Aggregate = "count_distinct" }, Condition = new RuleCondition { Operator = "gt", Value = 1 } } },
            JobRecurrenceKind.Daily, new TimeOnly(6, 0), 0),
        new(ManyEventsPerEntity, MonitoringRuleKind.Data, AlertSeverity.Medium,
            [new("entity", EntityRoles), new("time", [ColumnSemanticRole.Timestamp])],
            new RuleDefinition { Data = new DataRuleDefinition { WindowHours = 24, Measure = new RuleMeasure { Aggregate = "count" }, Condition = new RuleCondition { Operator = "gt", Value = 20 } } },
            JobRecurrenceKind.Daily, new TimeOnly(6, 0), 0),
        new(AmountAboveThreshold, MonitoringRuleKind.Data, AlertSeverity.Medium,
            [new("entity", EntityRoles), new("amount", [ColumnSemanticRole.Amount]), new("time", [ColumnSemanticRole.Timestamp])],
            new RuleDefinition { Data = new DataRuleDefinition { WindowHours = 24 * 7, Measure = new RuleMeasure { Aggregate = "sum" }, Condition = new RuleCondition { Operator = "gt", Value = 100000 } } },
            JobRecurrenceKind.Daily, new TimeOnly(6, 30), 0),
        new(MeasureChangeVersusPreviousPeriod, MonitoringRuleKind.Data, AlertSeverity.Medium,
            [new("measure", [ColumnSemanticRole.Amount, ColumnSemanticRole.Quantity]), new("time", [ColumnSemanticRole.Timestamp])],
            new RuleDefinition { Data = new DataRuleDefinition { WindowHours = 24 * 7, Measure = new RuleMeasure { Aggregate = "sum" }, Comparison = "previous_period", Condition = new RuleCondition { Operator = "lte", Value = -15 } } },
            JobRecurrenceKind.Weekly, new TimeOnly(7, 0), 0),
        new(RestrictedFilteredAccess, MonitoringRuleKind.Access, AlertSeverity.High, [],
            new RuleDefinition { Access = new AccessRuleDefinition { Signal = "identifier_filter_values", WindowHours = 24, MinClassification = DataClassification.Restricted, Threshold = new AccessThreshold { Mode = "absolute", Value = 10 } } },
            JobRecurrenceKind.Interval, default, 60, RequiresRestrictedColumns: true),
        new(RepeatedUnauthorizedAttempts, MonitoringRuleKind.Access, AlertSeverity.High, [],
            new RuleDefinition { Access = new AccessRuleDefinition { Signal = "unauthorized", WindowHours = 24, Threshold = new AccessThreshold { Mode = "absolute", Value = 2 } } },
            JobRecurrenceKind.Interval, default, 60),
        new(ExportsAboveBaseline, MonitoringRuleKind.Access, AlertSeverity.Medium, [],
            new RuleDefinition { Access = new AccessRuleDefinition { Signal = "exports", WindowHours = 24, Threshold = new AccessThreshold { Mode = "baseline", Value = 3, Statistic = "p95", MinActiveDays = 5, Minimum = 5 } } },
            JobRecurrenceKind.Daily, new TimeOnly(7, 0), 0),
        new(QueriesAboveBaseline, MonitoringRuleKind.Access, AlertSeverity.Medium, [],
            new RuleDefinition { Access = new AccessRuleDefinition { Signal = "rows", WindowHours = 24, Threshold = new AccessThreshold { Mode = "baseline", Value = 5, Statistic = "p95", MinActiveDays = 5, Minimum = 1000 } } },
            JobRecurrenceKind.Daily, new TimeOnly(7, 0), 0),
        new(SensitiveOutsideWorkingHours, MonitoringRuleKind.Access, AlertSeverity.Medium, [],
            new RuleDefinition { Access = new AccessRuleDefinition { Signal = "accesses", WindowHours = 24, MinClassification = DataClassification.Confidential, OutsideWorkingHours = true, Threshold = new AccessThreshold { Mode = "absolute", Value = 0.5m } } },
            JobRecurrenceKind.Daily, new TimeOnly(7, 0), 0),
        new(NewUserSensitiveAccess, MonitoringRuleKind.Access, AlertSeverity.Medium, [],
            new RuleDefinition { Access = new AccessRuleDefinition { Signal = "accesses", WindowHours = 24, MinClassification = DataClassification.Restricted, NewUsersWithinDays = 7, Threshold = new AccessThreshold { Mode = "absolute", Value = 0.5m } } },
            JobRecurrenceKind.Daily, new TimeOnly(7, 0), 0, RequiresRestrictedColumns: true),
    ];

    public static RuleTemplate? Find(string? code) => All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));

    /// <summary>
    /// Builds the data definition of a template from the chosen bindings (slot → column id). The first slot that
    /// identifies the entity becomes the group-by; the device or amount slot feeds the measure; the time slot the window.
    /// </summary>
    public static DataRuleDefinition BindData(RuleTemplate template, Guid objectId, IReadOnlyDictionary<string, Guid> bindings, DataRuleDefinition? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(bindings);
        var defaults = template.Defaults.Data ?? throw new ArgumentException("Not a data template.", nameof(template));
        Guid Slot(string slot) => bindings.TryGetValue(slot, out var id) ? id : throw new ArgumentException($"rule_slot_missing:{slot}", nameof(bindings));
        var definition = new DataRuleDefinition
        {
            ObjectId = objectId,
            TimeColumnId = Slot("time"),
            WindowHours = overrides?.WindowHours ?? defaults.WindowHours,
            Measure = new RuleMeasure { Aggregate = defaults.Measure.Aggregate },
            Condition = new RuleCondition { Operator = overrides?.Condition.Operator ?? defaults.Condition.Operator, Value = overrides?.Condition.Value ?? defaults.Condition.Value },
            Comparison = defaults.Comparison,
            Filters = overrides?.Filters ?? [],
            MaxGroups = overrides?.MaxGroups ?? defaults.MaxGroups,
        };

        switch (template.Code)
        {
            case MultiDeviceSameAccount:
                definition.GroupByColumnIds = [Slot("account")];
                definition.Measure.ColumnId = Slot("device");
                break;
            case ManyEventsPerEntity:
                definition.GroupByColumnIds = [Slot("entity")];
                break;
            case AmountAboveThreshold:
                definition.GroupByColumnIds = [Slot("entity")];
                definition.Measure.ColumnId = Slot("amount");
                break;
            case MeasureChangeVersusPreviousPeriod:
                definition.Measure.ColumnId = Slot("measure");
                break;
            default:
                throw new ArgumentException("Unknown data template.", nameof(template));
        }

        return definition;
    }
}
