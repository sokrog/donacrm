using Microsoft.AspNetCore.Components;

namespace Dona.Crm.UI.Components;

public sealed record DonaHeaderAction(string Label, EventCallback Callback, bool Disabled = false);
