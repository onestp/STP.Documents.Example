/*
This example demonstrates how to acquire an STP.Identity access token with nothing but a plain
HttpClient — no STP SDKs involved. It shows the two grants against the STP.Identity endpoints:

  * Password()  — resource owner password grant: posts client and user credentials directly to
                  the token endpoint and receives an access token and a refresh token.
  * OAuth()     — authorization code grant with PKCE: opens the authorization URL in the browser,
                  catches the redirect on a local HttpListener and exchanges the authorization
                  code (plus the PKCE code verifier) for tokens.

It exists to make the raw protocol visible, so the flows can be reproduced in languages and
runtimes that have no STP SDK. Do not take it as the recommended way to authenticate in .NET.

For .NET, use the STP.UserManagement.Identity.Client NuGet package instead. It offers a far more
developer friendly API and handles everything this file spells out by hand: the grant types
(ResourceOwnerPasswordCredentials, ClientCredentials, DeviceCredentials), token caching and
refresh (ITokenCache / TokenCacheFile), and attaching the access token to outgoing requests
(SetAccessToken). See Universal/Program.cs for how it is wired up via dependency injection.

$env:STP_CLIENT_ID     = "..."
$env:STP_CLIENT_SECRET = "..."
$env:STP_TENANT_NAME   = "..."
$env:STP_USERNAME      = "..."
$env:STP_PASSWORD      = "..."
dotnet run Auth.cs
 */

#:package System.IdentityModel.Tokens.Jwt@8.22.0

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

//await Password();
await OAuth();




async Task Password()
{
    string tokenUrl = Env("STP_TOKEN_URL", $"https://{Require("STP_TENANT_NAME")}.stp-cloud.de/identity/connect/token");
    var form = new Dictionary<string, string>
    {
        ["grant_type"] = "password",
        ["client_id"] = Require("STP_CLIENT_ID"),
        ["client_secret"] = Require("STP_CLIENT_SECRET"),
        ["username"] = Require("STP_USERNAME"),
        ["password"] = Require("STP_PASSWORD"),
    };

    using var http = new HttpClient();
    using var tokenResp = await http.PostAsync(tokenUrl, new FormUrlEncodedContent(form));
    var tokenBody = await tokenResp.Content.ReadAsStringAsync();

    if (!tokenResp.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"✗ Token request failed: {(int)tokenResp.StatusCode} {tokenResp.ReasonPhrase}");
        Console.Error.WriteLine(tokenBody);
    }
    else
    {
        using var tokenDoc = JsonDocument.Parse(tokenBody);
        string accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString()!;
        string refreshToken = tokenDoc.RootElement.GetProperty("refresh_token").GetString()!;
        Console.WriteLine($"✓ Got access token ({accessToken.Length} chars) and refresh token ({refreshToken.Length} chars).");

        var handler = new JwtSecurityTokenHandler();
        if (handler.CanReadToken(accessToken))
        {
            JwtSecurityToken jwt = handler.ReadJwtToken(accessToken);
            foreach (var claim in jwt.Claims)
            {
                Console.WriteLine($"{claim.Type,-10} = {claim.Value}");
            }
        }
        else
        {
            Console.WriteLine("(Access token is not a JWT — nothing to decode.)");
        }
    }
}





async Task OAuth()
{
    string callbackUrl = "http://localhost:11337/oauthcallback";
    string authorizeUrl = Env("STP_AUTHORIZE_URL", $"https://common.stp-cloud.de/identity/connect/authorize");
    var state = Guid.NewGuid().ToString("N");
    var nonce = Convert.ToBase64String(CreateRandomKey(64));
    var verifier = Convert.ToBase64String(CreateRandomKey(64));
    var challenge = Sha256OfCodeVerifier(verifier);

    var queryParams = new Dictionary<string, string>
    {
        { "client_id", Uri.EscapeDataString(Require("STP_CLIENT_ID")) },
        { "response_type", "code" },
        { "redirect_uri", Uri.EscapeDataString(callbackUrl) },
        { "scope", string.Join("+", (new []{"*", "offline_access"}).Select(s => Uri.EscapeDataString(s))) },
        { "state", Uri.EscapeDataString(state) },
        { "nonce", Uri.EscapeDataString(nonce) },
        { "code_challenge", Uri.EscapeDataString(challenge) },
        { "code_challenge_method", "S256" }
    };

    var authorizationUrl = new Uri($"{authorizeUrl}?{string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

    Console.WriteLine($"Opening browser for authorization: {authorizationUrl}");

    var listenerPrefix = new Uri(callbackUrl).GetLeftPart(UriPartial.Authority);
    if (!listenerPrefix.EndsWith("/")) listenerPrefix += "/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(listenerPrefix);

    try
    {
        listener.Start();

        var context = await listener.GetContextAsync();
        Console.WriteLine($"Received callback request: {context.Request.Url}");
        var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
        foreach (var key in query.AllKeys)
        {
            Console.WriteLine($"  {key} = {query[key]}");
        }
        var code = query["code"];
        var error = query["error"];

        string responseHtml = "<html><body><h1>Authentication complete</h1><p>You can close this window now.</p></body></html>";
        byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentLength64 = buffer.Length;
        context.Response.ContentType = "text/html";
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.Close();

        if (!string.IsNullOrEmpty(error))
        {
            throw new Exception($"Authorization failed: {error}");
        }

        if (string.IsNullOrEmpty(code))
        {
            throw new Exception($"No authorization code received");
        }

        string tokenUrl = Env("STP_TOKEN_URL", $"https://common.stp-cloud.de/identity/connect/token");
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = Require("STP_CLIENT_ID"),
            ["client_secret"] = Require("STP_CLIENT_SECRET"),
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = callbackUrl,
        };

        using var http = new HttpClient();
        using var tokenResp = await http.PostAsync(tokenUrl, new FormUrlEncodedContent(form));
        var tokenBody = await tokenResp.Content.ReadAsStringAsync();
        Console.WriteLine($"Exchanging authorization code for tokens...");

        if (!tokenResp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"✗ Token request failed: {(int)tokenResp.StatusCode} {tokenResp.ReasonPhrase}");
            Console.Error.WriteLine(tokenBody);
        }
        else
        {
            using var tokenDoc = JsonDocument.Parse(tokenBody);
            Console.WriteLine($"Token response: {tokenDoc.RootElement}");
            string accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString()!;
            string? refreshToken = tokenDoc.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement) ? refreshTokenElement.GetString()! : null;

            Console.WriteLine($"✓ Got access token ({accessToken.Length} chars) and refresh token ({refreshToken?.Length ?? 0} chars).");

            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(accessToken))
            {
                JwtSecurityToken jwt = handler.ReadJwtToken(accessToken);
                foreach (var claim in jwt.Claims)
                {
                    Console.WriteLine($"{claim.Type,-10} = {claim.Value}");
                }
            }
            else
            {
                Console.WriteLine("(Access token is not a JWT — nothing to decode.)");
            }
        }
    }
    catch (Exception ex)
    {
        throw new Exception($"Error getting auth code.", ex);
    }
    finally
    {
        if (listener.IsListening) listener.Stop();
    }

    static byte[] CreateRandomKey(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Create().GetBytes(bytes);
        return bytes;
    }

    static string Sha256OfCodeVerifier(string codeVerifier)
    {
        var codeVerifierBytes = Encoding.ASCII.GetBytes(codeVerifier);
        using (var sha = SHA256.Create())
        {
            var hashedBytes = sha.ComputeHash(codeVerifierBytes);
            var transformedCodeVerifier = Base64UrlEncode(hashedBytes);
            return transformedCodeVerifier;
        }

        static string Base64UrlEncode(byte[] arg)
        {
            var s = Convert.ToBase64String(arg); // Standard base64 encoder
            s = s.Split('=')[0]; // Remove any trailing '='s
            s = s.Replace('+', '-'); // 62nd char of encoding
            s = s.Replace('/', '_'); // 63rd char of encoding
            return s;
        }
    }
}







// --- helpers -----------------------------------------------------------------
static string Env(string key, string fallback) =>
    Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

static string Require(string key) =>
    Environment.GetEnvironmentVariable(key) is { Length: > 0 } v
        ? v
        : throw new InvalidOperationException($"Missing required environment variable: {key}");
