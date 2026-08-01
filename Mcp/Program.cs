using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

var host = Setup().Build();
using (var serviceScope = host.Services.CreateScope())
{
	var serviceProvider = serviceScope.ServiceProvider;
	var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
	try
	{
		var http = serviceProvider.GetRequiredService<HttpClient>();
		var config = serviceProvider.GetRequiredService<IConfiguration>();

		//create the mcp client
		var mcpServer = new Uri(config["Backend:McpServer"]!);
		await using var mcpClient = await McpClient.CreateAsync(new HttpClientTransport(new()
		{
			Name = "STP.Documents",
			Endpoint = mcpServer,
			OAuth = new ClientOAuthOptions
			{
				ClientId = config["Backend:ClientId"],
				RedirectUri = new Uri(config["Backend:RedirectUri"]!),
				AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
				TokenCache = new FileTokenCache(mcpServer),
			}
		}, http));

		//list mcp tools and engage...
		var mcpTools = await mcpClient.ListToolsAsync();
		Console.WriteLine("Available MCP tools:");
		foreach (var tool in mcpTools)
		{
			Console.WriteLine($"- {tool}");
		}

		//invoke a tool without arguments and print what it returned
		var result = await mcpClient.CallToolAsync("stp_doc_get_current_user");
		Console.WriteLine($"Result of stp_doc_get_current_user (IsError: {result.IsError}):");
		foreach (var content in result.Content.OfType<TextContentBlock>())
		{
			Console.WriteLine(content.Text);
		}
		if (result.StructuredContent is { } structured)
		{
			Console.WriteLine(JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true }));
		}

		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "An error occured!");
		return 1;
	}
}






static IHostBuilder Setup()
{
	return Host.CreateDefaultBuilder()
		.ConfigureAppConfiguration(cfg =>
		{
			cfg.AddJsonFile("appsettings.local.json", optional: true);
		})
		.UseSerilog((ctx, cfg) =>
		{
			cfg.ReadFrom.Configuration(ctx.Configuration);
		})
		.ConfigureServices((ctx, services) =>
		{
			services.AddHttpClient();
		});
}







//Taken from https://github.com/modelcontextprotocol/csharp-sdk/blob/main/samples/ProtectedMCPClient/Program.cs:
/// <summary>
/// Handles the OAuth authorization URL by starting a local HTTP server and opening a browser.
/// This implementation demonstrates how SDK consumers can provide their own authorization flow.
/// </summary>
/// <param name="context">The context carrying the authorization URI and the redirect URI.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>
/// The authorization response extracted from the callback, or null if the operation failed.
/// The <c>state</c> value must be returned so the SDK can bind the response to the request,
/// and <c>iss</c> should be returned when present so the SDK can validate the issuer (RFC 9207).
/// </returns>
static async Task<AuthorizationResult?> HandleAuthorizationUrlAsync(AuthorizationCallbackContext context, CancellationToken cancellationToken)
{
	Console.WriteLine("Starting OAuth authorization flow...");
	Console.WriteLine($"Opening browser to: {context.AuthorizationUri}");

	var listenerPrefix = context.RedirectUri.GetLeftPart(UriPartial.Authority);
	if (!listenerPrefix.EndsWith("/")) listenerPrefix += "/";

	using var listener = new HttpListener();
	listener.Prefixes.Add(listenerPrefix);

	try
	{
		listener.Start();
		Console.WriteLine($"Listening for OAuth callback on: {listenerPrefix}");

		OpenBrowser(context.AuthorizationUri);

		var callbackContext = await listener.GetContextAsync();
		var query = HttpUtility.ParseQueryString(callbackContext.Request.Url?.Query ?? string.Empty);
		var code = query["code"];
		var error = query["error"];

		string responseHtml = "<html><body><h1>Authentication complete</h1><p>You can close this window now.</p></body></html>";
		byte[] buffer = Encoding.UTF8.GetBytes(responseHtml);
		callbackContext.Response.ContentLength64 = buffer.Length;
		callbackContext.Response.ContentType = "text/html";
		callbackContext.Response.OutputStream.Write(buffer, 0, buffer.Length);
		callbackContext.Response.Close();

		if (!string.IsNullOrEmpty(error))
		{
			Console.WriteLine($"Auth error: {error}");
			return null;
		}

		if (string.IsNullOrEmpty(code))
		{
			Console.WriteLine("No authorization code received");
			return null;
		}

		Console.WriteLine("Authorization code received successfully.");
		return new AuthorizationResult
		{
			Code = code,
			State = query["state"],
			Iss = query["iss"],
		};
	}
	catch (Exception ex)
	{
		Console.WriteLine($"Error getting auth code: {ex.Message}");
		return null;
	}
	finally
	{
		if (listener.IsListening) listener.Stop();
	}
}

/// <summary>
/// Opens the specified URL in the default browser.
/// </summary>
/// <param name="url">The URL to open.</param>
static void OpenBrowser(Uri url)
{
	try
	{
		var psi = new ProcessStartInfo
		{
			FileName = url.ToString(),
			UseShellExecute = true
		};
		Process.Start(psi);
	}
	catch (Exception ex)
	{
		Console.WriteLine($"Error opening browser. {ex.Message}");
		Console.WriteLine($"Please manually open this URL: {url}");
	}
}

class FileTokenCache(Uri server) : ITokenCache
{
	private readonly string _file = $"{server.Authority.Replace(':', '_').Replace('.', '_')}.token";

	public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken = default) =>
		File.Exists(_file) ? JsonSerializer.Deserialize<TokenContainer>(await File.ReadAllTextAsync(_file, cancellationToken)) : null;

	public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken = default) =>
		await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(tokens), cancellationToken);
}
