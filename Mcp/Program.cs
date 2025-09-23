using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Text;
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
		await using var mcpClient = await McpClientFactory.CreateAsync(new SseClientTransport(new()
		{
			Name = "STP.Documents",
			Endpoint = new Uri(config["Backend:McpServer"]!),
			OAuth = new ClientOAuthOptions
			{
				ClientId = config["Backend:ClientId"],
				RedirectUri = new Uri(config["Backend:RedirectUri"]!),
				AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
			}
		}, http));

		//list mcp tools and engage...
		var mcpTools = await mcpClient.ListToolsAsync();
		Console.WriteLine("Available MCP tools:");
		foreach (var tool in mcpTools)
		{
			Console.WriteLine($"- {tool}");
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
/// <param name="authorizationUrl">The authorization URL to open in the browser.</param>
/// <param name="redirectUri">The redirect URI where the authorization code will be sent.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>The authorization code extracted from the callback, or null if the operation failed.</returns>
static async Task<string?> HandleAuthorizationUrlAsync(Uri authorizationUrl, Uri redirectUri, CancellationToken cancellationToken)
{
	Console.WriteLine("Starting OAuth authorization flow...");
	Console.WriteLine($"Opening browser to: {authorizationUrl}");

	var listenerPrefix = redirectUri.GetLeftPart(UriPartial.Authority);
	if (!listenerPrefix.EndsWith("/")) listenerPrefix += "/";

	using var listener = new HttpListener();
	listener.Prefixes.Add(listenerPrefix);

	try
	{
		listener.Start();
		Console.WriteLine($"Listening for OAuth callback on: {listenerPrefix}");

		OpenBrowser(authorizationUrl);

		var context = await listener.GetContextAsync();
		var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
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
			Console.WriteLine($"Auth error: {error}");
			return null;
		}

		if (string.IsNullOrEmpty(code))
		{
			Console.WriteLine("No authorization code received");
			return null;
		}

		Console.WriteLine("Authorization code received successfully.");
		return code;
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