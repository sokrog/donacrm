using Microsoft.AspNetCore.Components;

namespace Dona.Crm.UI.Components;

public sealed record DonaHeaderAction(string Label, EventCallback Callback = default, bool Disabled = false, string? Href = null);
