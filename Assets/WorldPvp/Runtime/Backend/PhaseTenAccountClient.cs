using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace WorldPvp.Phase1.Backend
{
    /// <summary>
    /// Supabase Auth / Vercel control-plane client. Only short-lived user tokens are stored locally;
    /// Supabase publishable/secret/database/server credentials are never shipped by this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhaseTenAccountClient : MonoBehaviour
    {
        private const string AccessTokenKey = "worldpvp.phase10.access-token";
        private const string RefreshTokenKey = "worldpvp.phase10.refresh-token";
        private const string AccessExpiryKey = "worldpvp.phase10.access-expiry";
        private const int DefaultAccessLifetimeSeconds = 3600;

        [SerializeField, Tooltip("Optional API root such as https://your-domain.vercel.app/api/v1. Web builds default to the current origin; Editor defaults to local Vercel dev.")]
        private string apiBaseUrl = string.Empty;
        [SerializeField, Range(60, 86400)] private int minimumAllowedAccessLifetimeSeconds = 60;

        [Serializable]
        private sealed class AuthUserDto
        {
            public string id;
            public string email;
        }

        [Serializable]
        private sealed class AuthResponseDto
        {
            public string access_token;
            public string refresh_token;
            public int expires_in;
            public string token_type;
            public AuthUserDto user;
            public bool needs_email_confirmation;
            public string message;
            public string error;
        }

        [Serializable]
        private sealed class ApiErrorDto
        {
            public string error;
            public string message;
        }

        [Serializable]
        public sealed class ProfileDto
        {
            public string id;
            public string username;
            public string avatar_url;
            public string created_at;
        }

        [Serializable]
        public sealed class StatisticsDto
        {
            public long matches_played;
            public long kills;
            public long deaths;
            public long wins;
        }

        [Serializable]
        public sealed class RecentMatchDto
        {
            public string match_id;
            public int kills;
            public int deaths;
            public int placement;
            public bool won;
            public int score;
            public string created_at;
        }

        [Serializable]
        private sealed class AccountResponseDto
        {
            public AuthUserDto user;
            public ProfileDto profile;
            public StatisticsDto statistics;
            public RecentMatchDto[] recent_matches;
        }

        [Serializable]
        private sealed class ProfileUpdateDto
        {
            public string username;
            public string avatarUrl;
        }

        private string accessToken = string.Empty;
        private string refreshToken = string.Empty;
        private string statusMessage = "Create an account or sign in to load your persistent profile.";
        private string accountId = string.Empty;
        private string email = string.Empty;
        private ProfileDto profile;
        private StatisticsDto statistics;
        private RecentMatchDto[] recentMatches = Array.Empty<RecentMatchDto>();
        private bool isBusy;
        private bool hasStarted;

        public event Action StateChanged;
        public bool IsSignedIn { get { return !string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(accountId); } }
        public bool IsBusy { get { return isBusy; } }
        public string StatusMessage { get { return statusMessage; } }
        public string AccountId { get { return accountId; } }
        public string Email { get { return email; } }
        public ProfileDto Profile { get { return profile; } }
        public StatisticsDto Statistics { get { return statistics; } }
        public RecentMatchDto[] RecentMatches { get { return recentMatches ?? Array.Empty<RecentMatchDto>(); } }
        public string ApiBaseUrl { get { return ResolveApiBaseUrl(); } }

        private void Start()
        {
            if (hasStarted) return;
            hasStarted = true;
            accessToken = PlayerPrefs.GetString(AccessTokenKey, string.Empty);
            refreshToken = PlayerPrefs.GetString(RefreshTokenKey, string.Empty);
            if (!string.IsNullOrEmpty(refreshToken))
            {
                StartCoroutine(RefreshAndLoadProfile());
            }
            else
            {
                RaiseStateChanged();
            }
        }

        public void SignUp(string emailAddress, string password, string username)
        {
            if (isBusy) return;
            string json = "{\"email\":" + JsonString(emailAddress == null ? string.Empty : emailAddress.Trim()) +
                         ",\"password\":" + JsonString(password ?? string.Empty) +
                         ",\"username\":" + JsonString(username == null ? string.Empty : username.Trim()) + "}";
            StartCoroutine(SubmitAuth("auth/signup", json));
        }

        public void SignIn(string emailAddress, string password)
        {
            if (isBusy) return;
            string json = "{\"email\":" + JsonString(emailAddress == null ? string.Empty : emailAddress.Trim()) +
                         ",\"password\":" + JsonString(password ?? string.Empty) + "}";
            StartCoroutine(SubmitAuth("auth/signin", json));
        }

        public void SignOut()
        {
            if (isBusy) return;
            if (!string.IsNullOrEmpty(accessToken))
            {
                StartCoroutine(SignOutRequest(accessToken));
            }
            ClearSession("Signed out.");
        }

        public void RefreshProfile()
        {
            if (isBusy) return;
            if (!IsSignedIn)
            {
                statusMessage = "Sign in to load the persistent account profile.";
                RaiseStateChanged();
                return;
            }
            StartCoroutine(LoadProfile());
        }

        public void UpdateProfile(string username, string avatarUrl)
        {
            if (isBusy || !IsSignedIn) return;
            string json = "{\"username\":" + JsonString(username ?? string.Empty) +
                         ",\"avatarUrl\":" + JsonString(avatarUrl ?? string.Empty) + "}";
            StartCoroutine(UpdateProfileRequest(json));
        }

        private IEnumerator SubmitAuth(string path, string json)
        {
            isBusy = true;
            statusMessage = "Contacting the secure account service…";
            RaiseStateChanged();
            AuthResponseDto response = null;
            yield return SendRequest("POST", path, json, string.Empty,
                (body, code, failure) =>
                {
                    response = Parse<AuthResponseDto>(body);
                    if (code < 200 || code >= 300)
                    {
                        statusMessage = response != null && !string.IsNullOrWhiteSpace(response.message)
                            ? response.message
                            : failure;
                    }
                });

            if (response != null && response.needs_email_confirmation)
            {
                statusMessage = string.IsNullOrWhiteSpace(response.message)
                    ? "Check your email to confirm the account, then sign in."
                    : response.message;
            }
            else if (!string.IsNullOrEmpty(response != null ? response.access_token : string.Empty))
            {
                ApplySession(response);
                statusMessage = "Account signed in. Loading your persistent profile…";
                isBusy = false;
                yield return LoadProfile();
                yield break;
            }
            else if (string.IsNullOrEmpty(statusMessage) || statusMessage == "Contacting the secure account service…")
            {
                statusMessage = "Sign-in did not return a session. Check your credentials and email confirmation.";
            }

            isBusy = false;
            RaiseStateChanged();
        }

        private IEnumerator RefreshAndLoadProfile()
        {
            isBusy = true;
            statusMessage = "Refreshing the short-lived account session…";
            RaiseStateChanged();
            AuthResponseDto response = null;
            string failure = string.Empty;
            yield return SendRequest("POST", "auth/refresh", "{\"refresh_token\":" + JsonString(refreshToken) + "}",
                string.Empty,
                (body, code, error) =>
                {
                    response = Parse<AuthResponseDto>(body);
                    failure = error;
                });
            if (response == null || string.IsNullOrEmpty(response.access_token))
            {
                ClearSession(string.IsNullOrWhiteSpace(failure)
                    ? "Your account session expired. Sign in again."
                    : "Your account session could not be refreshed. Sign in again.");
                isBusy = false;
                RaiseStateChanged();
                yield break;
            }
            ApplySession(response);
            isBusy = false;
            yield return LoadProfile();
        }

        private IEnumerator LoadProfile()
        {
            isBusy = true;
            statusMessage = "Loading your account, statistics, and match history…";
            RaiseStateChanged();
            AccountResponseDto response = null;
            string failure = string.Empty;
            long status = 0;
            yield return SendRequest("GET", "me", null, accessToken,
                (body, code, error) =>
                {
                    status = code;
                    failure = error;
                    response = Parse<AccountResponseDto>(body);
                });
            if (status == 401)
            {
                isBusy = false;
                ClearSession("Your account session expired. Sign in again.");
                yield break;
            }
            if (response == null || response.profile == null)
            {
                statusMessage = string.IsNullOrWhiteSpace(failure)
                    ? "The persistent account profile could not be loaded."
                    : failure;
                isBusy = false;
                RaiseStateChanged();
                yield break;
            }
            accountId = response.user != null ? response.user.id : string.Empty;
            email = response.user != null ? response.user.email : string.Empty;
            profile = response.profile;
            statistics = response.statistics ?? new StatisticsDto();
            recentMatches = response.recent_matches ?? Array.Empty<RecentMatchDto>();
            statusMessage = "Persistent account loaded.";
            isBusy = false;
            RaiseStateChanged();
        }

        private IEnumerator UpdateProfileRequest(string json)
        {
            isBusy = true;
            statusMessage = "Saving profile changes…";
            RaiseStateChanged();
            string failure = string.Empty;
            long status = 0;
            yield return SendRequest("PATCH", "me", json, accessToken,
                (body, code, error) => { status = code; failure = error; });
            isBusy = false;
            if (status == 401)
            {
                ClearSession("Your account session expired. Sign in again.");
                yield break;
            }
            statusMessage = status >= 200 && status < 300
                ? "Profile saved. Refreshing account data…"
                : (string.IsNullOrWhiteSpace(failure) ? "Profile changes were not saved." : failure);
            RaiseStateChanged();
            if (status >= 200 && status < 300) yield return LoadProfile();
        }

        private IEnumerator SignOutRequest(string token)
        {
            yield return SendRequest("POST", "auth/signout", "{}", token, (body, code, error) => { });
        }

        private IEnumerator SendRequest(
            string method,
            string path,
            string json,
            string token,
            Action<string, long, string> completed)
        {
            string baseUrl = ResolveApiBaseUrl();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                completed(string.Empty, 0, "Set the Vercel API origin, or run the local Vercel development server.");
                yield break;
            }

            UnityWebRequest request = new UnityWebRequest(baseUrl + "/" + path.TrimStart('/'), method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 15;
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.SetRequestHeader("Authorization", "Bearer " + token);
            }

            yield return request.SendWebRequest();
            string responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            long status = request.responseCode;
            string message = string.Empty;
            if (request.result != UnityWebRequest.Result.Success)
            {
                ApiErrorDto errorDto = Parse<ApiErrorDto>(responseBody);
                message = errorDto != null && !string.IsNullOrWhiteSpace(errorDto.message)
                    ? errorDto.message
                    : "Network request failed. Check the secure connection and backend deployment.";
            }
            completed(responseBody, status, message);
            request.Dispose();
        }

        private void ApplySession(AuthResponseDto response)
        {
            accessToken = response.access_token ?? string.Empty;
            refreshToken = response.refresh_token ?? string.Empty;
            accountId = response.user != null ? response.user.id ?? string.Empty : string.Empty;
            email = response.user != null ? response.user.email ?? string.Empty : string.Empty;
            int lifetime = Mathf.Clamp(
                response.expires_in > 0 ? response.expires_in : DefaultAccessLifetimeSeconds,
                Mathf.Max(60, minimumAllowedAccessLifetimeSeconds),
                86400);
            long expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + lifetime;
            PlayerPrefs.SetString(AccessTokenKey, accessToken);
            PlayerPrefs.SetString(RefreshTokenKey, refreshToken);
            PlayerPrefs.SetString(AccessExpiryKey, expiry.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        private void ClearSession(string message)
        {
            accessToken = string.Empty;
            refreshToken = string.Empty;
            accountId = string.Empty;
            email = string.Empty;
            profile = null;
            statistics = null;
            recentMatches = Array.Empty<RecentMatchDto>();
            PlayerPrefs.DeleteKey(AccessTokenKey);
            PlayerPrefs.DeleteKey(RefreshTokenKey);
            PlayerPrefs.DeleteKey(AccessExpiryKey);
            PlayerPrefs.Save();
            statusMessage = message;
            RaiseStateChanged();
        }

        private string ResolveApiBaseUrl()
        {
            string configured = (apiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(configured))
            {
                Uri configuredUri;
                if (!Uri.TryCreate(configured, UriKind.Absolute, out configuredUri) ||
                    (configuredUri.Scheme != Uri.UriSchemeHttps && configuredUri.Scheme != Uri.UriSchemeHttp))
                {
                    return string.Empty;
                }
#if UNITY_WEBGL && !UNITY_EDITOR
                if (configuredUri.Scheme != Uri.UriSchemeHttps) return string.Empty;
#endif
                return configured.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)
                    ? configured
                    : configured + "/api/v1";
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            Uri pageUri;
            if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out pageUri) &&
                pageUri.Scheme == Uri.UriSchemeHttps)
            {
                return pageUri.GetLeftPart(UriPartial.Authority) + "/api/v1";
            }
            return string.Empty;
#else
            return "http://localhost:3000/api/v1";
#endif
        }

        private void RaiseStateChanged()
        {
            Action changed = StateChanged;
            if (changed != null) changed.Invoke();
        }

        private static string JsonString(string value)
        {
            string wrapped = JsonUtility.ToJson(new StringValue { value = value ?? string.Empty });
            int colon = wrapped.IndexOf(':');
            if (colon < 0 || wrapped.Length <= colon + 1) return "\"\"";
            return wrapped.Substring(colon + 1, wrapped.Length - colon - 2);
        }

        private static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception) { return null; }
        }

        [Serializable]
        private sealed class StringValue
        {
            public string value;
        }
    }
}
