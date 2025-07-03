using STP.Ecm.ApiCore;
using STP.Ecm.Dto.Container;
using STP.Ecm.Dto.Document;
using STP.Ecm.Dto.Topics;
using STP.Lsb;

var config = new Config();
var dms = new Dms(config);
try
{
	Console.WriteLine("Login...");
	var initResult = dms.Init(config.UmServerUser, config.UmPassword);

	Console.WriteLine("Impersonate user...");
	using (dms.Impersonate(new Guid("...")))
	{
		Console.WriteLine("Load container...");
		{
			var containerId = (await dms.Container.SearchContainersByString("%")).Take(1).ToList();
			var container = (await dms.Container.LoadContainers(containerId)).First();
			Console.WriteLine(container switch
			{
				DmsDossierDto dossier => $"{dossier.AzIntern} {dossier.Bezeichnung} (Dossier)",
				DmsFolderDto folder => $"{folder.Name} (Folder)",
				_ => string.Empty
			});

			Console.WriteLine("Loading documents...");
			{
				var documentIds = (await dms.Container.GetDocumentIdsForContainer(container.Id)).Take(50).ToList();
				var documents = await dms.Document.LoadDocumentData(documentIds);
				foreach (var document in documents)
				{
					Console.WriteLine(document.Title);
				}
			}
		}

		Console.WriteLine("Upload document...");
		{
			var spielwieseId = await dms.Container.SearchContainersByString("Spielwiese");
			var spielwiese = (await dms.Container.LoadContainers(spielwieseId)).Single();
			var file = new FileInfo("HalloWelt.docx");

			var documentDto = new DmsDocumentDto
			{
				Title = "Hallo Welt",
				ContainerRelations = new List<DmsDocumentContainerRelationDto>
				{
					new DmsDocumentContainerRelationDto
					{
						ContainerId = spielwiese.Id,
						TrayId = 0
					}
				},
				Versions = new List<DmsDocumentVersionDto>
				{
					new DmsDocumentVersionDto
					{
						Title = "Hallo Welt",
						Comment = "first version",
						Filename = file.Name,
						Extension = file.Extension.Trim('.').ToLower()
					}
				},
				Kommentar = "first version",
			};
			var topicName = spielwiese.IndexDatas.FirstOrDefault()?.Name;
			if (topicName != null)
			{
				documentDto.IndexDatas = new[] { new DmsTopicInfoDto(topicName) };
			}

			var documentId = await dms.Document.Import(file, documentDto);

			Console.WriteLine("Upload new version...");
			{
				var documentData = await dms.Document.LoadDocumentData(documentId);
				var edit = await dms.Document.Checkout(documentData, $"{documentId}_{DateTime.Now.Ticks}.{documentData.Versions.Last().Extension}");
				edit.FinishWithNewVersion("new version");
			}
		}
	}
}
catch (Exception ex)
{
	Console.WriteLine(ex.ToString());
}
finally
{
	dms.Dispose();
}



public class Config : ILsbConfiguration
{
	public string RabbitMqHostname { get; set; } = "";
	public int RabbitMqPort { get; set; } = 5672;
	public string RabbitMqUsername { get; set; } = "";
	public string RabbitMqPassword { get; set; } = "";
	public string UmServerUser { get; set; } = "";
	public string UmPassword { get; set; } = "";

	public string PersistentIdentifier { get; set; } = null!;
	public int PersistentPrefetchCount { get; set; }
	public string TransientIdentifier { get; set; } = null!;
	public int TransientPrefetchCount { get; set; }
	public bool UsePersistentQueue { get; set; }
	public LsbRetryMode PersistentRetryMode { get; set; }
}
