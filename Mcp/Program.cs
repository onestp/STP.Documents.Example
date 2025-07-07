using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Serilog;
using STP.UserManagement.Identity.Client;
using System.Runtime.InteropServices;

var host = Setup().Build();
using (var serviceScope = host.Services.CreateScope())
{
	var serviceProvider = serviceScope.ServiceProvider;
	var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
	try
	{
		//get the authenticated http client from the service provider
		var http = serviceProvider.GetRequiredService<HttpClient>();

		//create the mcp client
		await using var mcpClient = await McpClientFactory.CreateAsync(new SseClientTransport(new()
		{
			Name = "STP.Documents",
			Endpoint = new Uri(serviceProvider.GetRequiredService<IConfiguration>()["Backend:McpServer"]!),
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
			// Setup for STP.Identity
			services.AddTransient<SetStpSubdomainHeader>();
			services.AddTransient<SetAccessToken>();

			services.AddSingleton<ITokenCache>(sp => //or just TokenCache for in-memory caching
			{
				var useProtection = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
				if (!useProtection)
				{
					var logger = sp.GetRequiredService<ILogger<Program>>();
					logger.LogWarning("Windows DPAPI is not available, therefore your access token cache file is not encrypted!");
				}

				return new TokenCacheFile($"./stp-doc-example-{new Uri(ctx.Configuration["Backend:Authority"]!).Host.Replace(".", "")}.tokens", useProtection);
			});

			if (string.Equals(ctx.Configuration["Backend:ClientType"], "device", StringComparison.InvariantCultureIgnoreCase))
			{
				services.AddSingleton<DeviceCredentials.AuthorizeCallback, DeviceCredentials.AuthorizeCallback.InConsole>();
				services.AddHttpClient<ITokenProvider, DeviceCredentials>();
			}
			else if (string.Equals(ctx.Configuration["Backend:ClientType"], "password", StringComparison.InvariantCultureIgnoreCase))
			{
				services.AddHttpClient<ITokenProvider, ResourceOwnerPasswordCredentials>().AddHttpMessageHandler<SetStpSubdomainHeader>();
			}
			else if (string.Equals(ctx.Configuration["Backend:ClientType"], "client_credentials", StringComparison.InvariantCultureIgnoreCase))
			{
				services.AddHttpClient<ITokenProvider, ClientCredentials>().AddHttpMessageHandler<SetStpSubdomainHeader>();
			}
			else
			{
				throw new ArgumentOutOfRangeException("Backend:ClientType", ctx.Configuration["Backend:ClientType"], "unknown value");
			}

			services.Configure<TokenProviderOptions>(ctx.Configuration.GetSection("Backend"));
			services.Configure<StpSubdomainHeaderOptions>(ctx.Configuration.GetSection("Backend"));

			// Set the access token handler to the default http client
			services.AddHttpClient("").AddHttpMessageHandler<SetAccessToken>();
		});
}
