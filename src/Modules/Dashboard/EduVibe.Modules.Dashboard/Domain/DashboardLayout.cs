using EduVibe.Shared.Domain;
using EduVibe.Shared.Tenancy;

namespace EduVibe.Modules.Dashboard.Domain;

/// <summary>
/// How one teacher arranged their start screen: which widgets are on and in what order. The server keeps only keys and on/off flags,
/// so a new widget is a change in the web app alone; keys it has not saved yet fall in at the end with their own defaults.
/// </summary>
public sealed class DashboardLayout : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>A JSON array of <c>{ "key": "...", "enabled": true }</c> in display order.</summary>
    public string WidgetsJson { get; set; } = "[]";
}
