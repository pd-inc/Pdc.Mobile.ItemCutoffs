namespace Pdc.Mobile.ItemCutoffs.Repositories.Abstract;

/// <summary>
/// Mints a short-lived OAuth2 access token for Firebase REST calls. Behind an
/// interface so the repository's HTTP shape is testable without a Firebase app.
/// </summary>
public interface IFirebaseAccessTokenProvider
{
    Task<string> GetAccessTokenAsync();
}
