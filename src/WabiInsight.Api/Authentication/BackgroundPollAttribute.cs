namespace WabiInsight.Api.Authentication;

/// <summary>
/// Marks an endpoint the web app calls on a timer without the person doing anything (the unread badge, the
/// support-mode banner). Such a request is answered like any other but never extends the session: the idle
/// timeout (specification §8) counts from the person's last own action, not from the last poll.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BackgroundPollAttribute : Attribute;
