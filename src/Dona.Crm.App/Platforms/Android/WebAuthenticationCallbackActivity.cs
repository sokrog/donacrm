using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Dona.Crm.App;

[Activity(NoHistory = true, Exported = true, LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "donacrm",
    DataHost = "oauth2redirect")]
public sealed class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity;
