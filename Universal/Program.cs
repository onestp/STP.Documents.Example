using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using STP.Documents.Client;
using STP.Documents.Client.Search;
using STP.Documents.Client.Universal;
using STP.Documents.Client.Universal.Metadata;
using STP.Documents.Client.Universal.Upload;
using STP.UserManagement.Identity.Client;
using System.Diagnostics;
using System.Runtime.InteropServices;

var host = Setup().Build();
using (var serviceScope = host.Services.CreateScope())
{
	var serviceProvider = serviceScope.ServiceProvider;
	var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
	try
	{
		var documentsClient = serviceProvider.GetRequiredService<IDocumentsClient>();

		await UploadToMatterAndDownloadDocument(documentsClient.Universal, logger);

		await SemanticSearchDocument(documentsClient, logger);

		return 0;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "An error occured!");
		return 1;
	}
}

/// <summary>
/// This method sets up dependency injection for an authenticated documents client.
/// </summary>
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

			// Setup for STP.Documents
			services.Configure<DocumentsClientOptions>(ctx.Configuration.GetSection("Backend"));
			services.AddDocumentsClient<SetAccessToken>();
		});
}

/// <summary>
/// This method uploads an "Hallo Welt" document to a matter.
/// If the matter does not exist, it creates a new one.
/// The document is downloaded again and opened in the default application.
/// </summary>
static async Task UploadToMatterAndDownloadDocument(IDocumentsUniversalClient documentsClient, Microsoft.Extensions.Logging.ILogger logger)
{
	var requestOptions = new RequestOptions { PersistenceSelector = "CloudStore" };

	var matterSearchResult = await documentsClient.SearchContexts(
		contextType: "stp.doc.matter",
		currentContexts: null,
		searchTerm: "m1",
		skip: 0,
		take: 100,
		requestOptions: requestOptions);

	var matter = matterSearchResult.Contexts.FirstOrDefault()?.ToReference();
	if (matter == null)
	{
		var newMatterM1 = await documentsClient.CreateContext(new NewContextRequestDto
		{
			Type = "stp.doc.matter",
			Name = "m1",
			DisplayName = "Matter 1",
		});
		matter = new ContextDto
		{
			Id = newMatterM1.Id,
			Name = newMatterM1.Name,
			DisplayName = newMatterM1.DisplayName,
		}.ToReference();
	}

	using var documentToUpload = File.OpenRead("HalloWelt.docx");
	var uploaded = await documentsClient.Upload(
		stream: documentToUpload,
		uploadRequest: new UploadRequestDto
		{
			ExpectedLength = documentToUpload.Length,
			Comment = "Mein Dokument",
			ContextReferences = [matter],
		},
		requestOptions: requestOptions);

	logger.LogInformation("Uploaded {File} as Document {Document} to matter {Matter}.", documentToUpload.Name, uploaded.DocumentId, matter);

	var documentsOfMatter = await documentsClient.Resolve(
		reference: matter,
		skip: 0,
		take: 100,
		requestOptions: requestOptions);

	var resolvedDocument = documentsOfMatter.Documents.FirstOrDefault(d => d.Id == uploaded.DocumentId);
	logger.LogInformation($"Document {{Document}} {(resolvedDocument != null ? "is" : "is not")} among {documentsOfMatter.TotalCount} documents of {{Matter}}.", uploaded.DocumentId, matter);

	var downloaded = await documentsClient.Download(resolvedDocument!.Id);
	var downloadedFileName = $"downloaded_{DateTime.Now.Ticks}_{resolvedDocument.Name}";
	await downloaded!.Stream.WriteFile(downloadedFileName);
	logger.LogInformation("Downloaded {Document} again.", resolvedDocument.Id);
	Process.Start(new ProcessStartInfo { FileName = downloadedFileName, UseShellExecute = true });
}

/// <summary>
/// This method searches for the uploaded document "Hallo Welt" using semantic search.
/// It assumes the tenant has AutoIndexEveryNewDocument enabled and indexing was successful.
/// </summary>
static async Task SemanticSearchDocument(IDocumentsClient documentsClient, Microsoft.Extensions.Logging.ILogger logger)
{
	var requestOptions = new RequestOptions { PersistenceSelector = "CloudStore" };

	var searchResults = await documentsClient.Search.SemanticSearch(
		request: new SemanticSearchRequest { Search = "Hi World", },
		requestOptions: requestOptions);

	if (searchResults.SimilarChunks.Count == 0)
	{
		logger.LogInformation("No documents found by semantic search.");
	}

	foreach (var chunk in searchResults.SimilarChunks)
	{
		logger.LogInformation("With {Similarity} similarity we found chunk {ChunkId} of document {DocumentId}", chunk.CosineSimilarity, chunk.ChunkId, chunk.DocumentId);
		Console.WriteLine(chunk.Chunk);
	}
}