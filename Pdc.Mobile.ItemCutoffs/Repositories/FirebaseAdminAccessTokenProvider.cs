using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Pdc.Mobile.ItemCutoffs.Repositories.Abstract;

namespace Pdc.Mobile.ItemCutoffs.Repositories;

/// <summary>
/// Mints a short-lived OAuth2 access token from the Firebase Admin SDK's default app
/// credential (the service account in configuration). Matches the Contests,
/// Notifications and AttendeeSync Lambdas.
/// </summary>
public class FirebaseAdminAccessTokenProvider : IFirebaseAccessTokenProvider
{
    private const string FirebaseScope = "https://www.googleapis.com/auth/firebase.database";
    private const string UserInfoScope = "https://www.googleapis.com/auth/userinfo.email";

    public async Task<string> GetAccessTokenAsync()
    {
        var app = FirebaseApp.DefaultInstance;
        var credential = app.Options.Credential.CreateScoped(FirebaseScope, UserInfoScope);
        var tokenAccess = (ITokenAccess)credential;
        return await tokenAccess.GetAccessTokenForRequestAsync();
    }
}
