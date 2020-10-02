using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Linq;
using Jurassic.Library;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Text;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace GetCapacity
{
  public static class GetCapacity
  {
    [FunctionName("GetCapacity")]
    public static void Run([TimerTrigger("0 */15 * * * *")] TimerInfo myTimer, ILogger log, ExecutionContext context)
    {
      log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now.TimeOfDay}");

      //if (!ShouldRun()) return;

      try
      {
        IConfigurationRoot config = GetConfig(log, context);

        string url = config["url"];
        string xpath = config["xpath"];

        HtmlWeb web = new HtmlWeb();
        var htmlDoc = web.Load(url);

        var script = htmlDoc.DocumentNode.Descendants()
                        .Where(n => n.Name == "script"
                            && n.XPath.Equals(xpath))
                        .First().InnerText;

        string scriptJson = script.Substring(0, script.IndexOf("function"));

        log.LogInformation("Parsing the returned HTML...");

        var engine = new Jurassic.ScriptEngine();
        var result = engine.Evaluate("(function() { " + scriptJson + " return data; })()");
        var capacityData = JSONObject.Stringify(engine, result).Replace("\\", string.Empty);

        string timestampUtc = DateTime.Now.ToString();
        string json = JsonConvert.SerializeObject(capacityData);

        CloudBlockBlob blob = GetBlobConnection(log, config);

        //download current json
        string existingData = blob.DownloadTextAsync(System.Text.Encoding.ASCII, null, new BlobRequestOptions { DisableContentMD5Validation = true }, new OperationContext()).Result;
        int existingDataLength = existingData.Length;
        log.LogInformation($"Existing data size {existingDataLength}");

        StringBuilder sb = new StringBuilder(existingData);
        if (sb.Length > 0)
        {
          sb.Remove(sb.Length - 1, 1); // remove ending '}'
          sb.Append(",");
        }
        else
        {
          sb.Append("{");
        }

        log.LogInformation($"Writing new data to blob... {json.Length}");

        sb.Append($"\"{timestampUtc}\": {json}");
        sb.Append("}");

        blob.UploadTextAsync(sb.ToString());
        log.LogInformation("Done!");
      }
      catch (Exception ex)
      {
        log.LogInformation($"Problem: {ex}");
      }
    }

    public static CloudBlockBlob GetBlobConnection(ILogger log, IConfigurationRoot config)
    {
      CloudBlockBlob retval = null;
      try
      {
        log.LogInformation("Connecting to block blob...");
        string blobConnectionString = config["BlobConnectionString"];
        string containerName = config["BlobContainerName"];

        CloudStorageAccount storageAccount = CloudStorageAccount.Parse(blobConnectionString);
        CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
        CloudBlobContainer container = blobClient.GetContainerReference(containerName);

        string blobName = string.Empty;
        if (ShouldCreateNewBlob(config))
        {
          //TODO: Consider timezone
          blobName = DateTime.Now.ToString("MM-dd-yyyy") + ".json";
          retval = container.GetBlockBlobReference(blobName);
          config["currentBlob"] = blobName;
          //TODO: update KV

          Task.Run(() => retval.UploadTextAsync(""));
        }
        else
        {
          blobName = GetBlobName(config);
          retval = container.GetBlockBlobReference(blobName);
        }
      }
      catch (Exception ex)
      {
        log.LogCritical($"Error: {ex}. {ex.Message}");
      }
      return retval;
    }

    public static string GetBlobName(IConfigurationRoot config)
    {
      return config["currentBlob"] != null ? config["currentBlob"] : "data.json";
    }

    public static bool ShouldCreateNewBlob(IConfigurationRoot config)
    {
      //TODO: Consider timezone
      bool shouldCreateNewBlob = false;
      DayOfWeek lastUpdate = (DayOfWeek)Enum.Parse(typeof(DayOfWeek),
      (config["lastUpdateDay"] != null) ? config["lastUpdateDay"] : DateTime.Now.DayOfWeek.ToString()); //TODO:

      if (DateTime.Now.DayOfWeek.Equals(DayOfWeek.Sunday) && lastUpdate.Equals(DayOfWeek.Saturday))
      {
        shouldCreateNewBlob = true;

      }
      return shouldCreateNewBlob;
    }

    public static bool ShouldRun()
    {
      //TODO: Consider timezone
      TimeSpan startTime = new TimeSpan(5, 0, 0);
      TimeSpan endTime = new TimeSpan(21, 30, 0);
      DateTime now = DateTime.Now;

      if (endTime == startTime)
      {
        return true;
      }
      else if (endTime < startTime)
      {
        return now.TimeOfDay <= endTime ||
            now.TimeOfDay >= startTime;
      }
      else
      {
        return now.TimeOfDay >= startTime &&
            now.TimeOfDay <= endTime;
      }
    }

    public static IConfigurationRoot GetConfig(ILogger log, ExecutionContext context)
    {
      try
      {
        log.LogInformation("Building out the config root...");
        IConfigurationBuilder configBuilder = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddEnvironmentVariables();

        IConfigurationRoot config = configBuilder.Build();

        configBuilder.AddAzureKeyVault(
            $"https://{config["AzureKeyVault:VaultName"]}.vault.azure.net/",
            config["AzureKeyVault:ClientId"],
            config["AzureKeyVault:ClientSecret"]
        );
        return configBuilder.Build();
      }
      catch (Exception ex)
      {
        log.LogCritical(ex.ToString());
        throw;
      }
    }
  }
}
