using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using STP.Documents.Client;
using STP.Documents.Client.CloudStore;
using STP.Documents.Client.CloudStore.Locate;
using STP.Documents.Client.CloudStore.Upload;
using STP.UserManagement.Identity.Client;
using System.Diagnostics;
using System.Runtime.InteropServices;

var host = CreateHostBuilder().Build();
using (var serviceScope = host.Services.CreateScope())
{
	var serviceProvider = serviceScope.ServiceProvider;
	var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
	try
	{
		var documentsClient = serviceProvider.GetRequiredService<IDocumentsCloudStoreClient>();
		var matter = (await documentsClient.FindContexts(searchTerm: "m1")).Items.ToList() switch
		{
			[var first, ..] => first,
			[] => await documentsClient.CreateContext(new NewContextDto(ContextType.Matter)
			{
				Name = "m1",
				DisplayName = "Matter 1"
			}),
		};

		using var document = File.OpenRead("HalloWelt.docx");
		var (uploaded, connected) = await documentsClient.Import(document, new UploadData { Comment = "Mein Dokument" }, new[] { matter });
		logger.LogInformation("Uploaded {File} as Document {Document} to matter {Matter}.", document.Name, uploaded.DocumentId, connected);

		var documentsOfMatter = await documentsClient.Resolve(matter);
		var uploadedDocumentIsAmongMatterDocuments = documentsOfMatter.Ids.Contains(uploaded.DocumentId);
		logger.LogInformation($"Document {{Document}} {(uploadedDocumentIsAmongMatterDocuments ? "is" : "is not")} among {documentsOfMatter.Count} documents of {{Matter}}.", uploaded.DocumentId, connected);

		var downloadedStream = await documentsClient.Download(uploaded);
		using var writer = File.OpenWrite($"downloaded_{DateTime.Now.Ticks}_{uploaded.Filename}");
		await downloadedStream.CopyToAsync(writer);
		writer.Close();
		logger.LogInformation("Downloaded {Document} again.", uploaded.DocumentId);
		Process.Start(new ProcessStartInfo { FileName = writer.Name, UseShellExecute = true });
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "An error occured!");
	}
}

static IHostBuilder CreateHostBuilder()
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
			services.AddTransient<SetStpSubdomainHeader>();
			services.AddTransient<SetAccessToken>();
			
			services.AddSingleton<ITokenCache>(sp =>
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

			services.Configure<DocumentsClientOptions>(ctx.Configuration.GetSection("Backend"));
			services.AddDocumentsClient<SetAccessToken>();
		});
}