using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyVersion("0.0.0.0")]
namespace Anker.Zombie;

public class App : IExternalApplication
{
	private static readonly List<IExternalApplication> LoadedApps = new List<IExternalApplication>();

	private string ClientId = "PUBLIC";

	private string GetRegistryValue(string key, string fallback = "", bool admin = true)
	{
		string obj = (admin ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER");
		string keyName = obj + "\\Software\\Anker";
		string keyName2 = obj + "\\Software\\Reope\\Anker";
		string text = Registry.GetValue(keyName, key, "")?.ToString();
		if (string.IsNullOrWhiteSpace(text))
		{
			text = Registry.GetValue(keyName2, key, "")?.ToString();
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			text = fallback;
		}
		return text;
	}

	public Result OnStartup(UIControlledApplication application)
	{
		ClientId = GetRegistryValue("DOKK_CLIENT", "PUBLIC");
		string directoryName = Path.GetDirectoryName(Environment.GetEnvironmentVariable("temp"));
		string directoryName2 = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		string text = Path.Combine(directoryName, "Anker");
		string path = ((int.Parse(application.ControlledApplication.VersionNumber) > 2024) ? "net8.0-windows" : "net4.8");
		string path2 = Path.Combine(directoryName2, path);
		if (!Directory.Exists(text))
		{
			Directory.CreateDirectory(text);
		}
		else
		{
			foreach (string item in Directory.EnumerateFiles(text))
			{
				try
				{
					File.Delete(item);
				}
				catch (Exception)
				{
				}
			}
		}
		Updater updater = new Updater(Assembly.GetExecutingAssembly().Location, "addin");
		if (updater.IsFirstTimeUpdate)
		{
			updater.CheckAndDownloadUpdatesBlocking(ClientId);
		}
		string[] files = Directory.GetFiles(path2);
		foreach (string text2 in files)
		{
			string text3 = Path.Combine(text, Path.GetFileName(text2));
			try
			{
				File.Copy(text2, text3, overwrite: true);
			}
			catch (Exception)
			{
			}
			if (text2 != Assembly.GetExecutingAssembly().Location && Path.GetExtension(text2) == ".dll")
			{
				if (Path.GetFileName(text2).StartsWith("Anker"))
				{
					LoadedApps.AddRange(LoadExternalApp(text3));
				}
				else
				{
					Assembly.LoadFile(text3);
				}
			}
		}
		OnStartupAllLoaded(application);
		return (Result)0;
	}

	public Result OnShutdown(UIControlledApplication application)
	{
		new Updater(Assembly.GetExecutingAssembly().Location, "addin").CheckAndDownloadUpdatesBlocking(ClientId);
		OnShutDownAllLoaded(application);
		return (Result)0;
	}

	private static IExternalApplication[] LoadExternalApp(string assemblyPath)
	{
		return (from t in Assembly.LoadFile(assemblyPath).GetTypes()
			where t.GetInterfaces().Contains(typeof(IExternalApplication))
			select t).Select(delegate(Type x)
		{
			object obj = Activator.CreateInstance(x);
			return (IExternalApplication)((obj is IExternalApplication) ? obj : null);
		}).ToArray();
	}

	private static void OnStartupAllLoaded(UIControlledApplication application)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		foreach (IExternalApplication loadedApp in LoadedApps)
		{
			loadedApp.OnStartup(application);
		}
	}

	private static void OnShutDownAllLoaded(UIControlledApplication application)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		foreach (IExternalApplication loadedApp in LoadedApps)
		{
			loadedApp.OnShutdown(application);
		}
	}
}
public class UpdateInfo
{
	[JsonProperty("app")]
	public string App { get; set; }

	[JsonProperty("installed_version")]
	public string InstalledVersion { get; set; }

	[JsonProperty("latest_version")]
	public string LatestVersion { get; set; }

	[JsonProperty("update_actions")]
	public List<UpdateAction> UpdateActions { get; set; }

	[JsonProperty("install_actions")]
	public List<UpdateAction> InstallActions { get; set; }

	[JsonProperty("uninstall_actions")]
	public List<UpdateAction> UninstallActions { get; set; }

	[JsonProperty("release")]
	public Release Release { get; set; }
}
public class UpdateAction
{
	[JsonProperty("file_name")]
	public string FileName { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public ActionType Type { get; set; }

	[JsonProperty("location")]
	public string Location { get; set; }
}
public enum ActionType
{
	Copy,
	Extract,
	Delete,
	Run
}
public class UpdateAvailability
{
	[JsonProperty("is_update_available")]
	public bool IsUpdateAvailable;

	[JsonProperty("client_id")]
	public string ClientID { get; set; }

	[JsonProperty("client_name")]
	public string ClientName { get; set; }

	[JsonProperty("update_info")]
	public List<UpdateInfo> UpdateInfo { get; set; }
}
public class Release
{
	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("files")]
	public string Binaries { get; set; }

	[JsonIgnore]
	public List<string> FileNames => Binaries.Split(',').ToList();
}
public class DokkResponse
{
	[JsonProperty("errors")]
	public object Errors;

	[JsonProperty("data")]
	public UpdateAvailability Data;
}
public class Updater
{
	private const string UpdateInfoFileName = "update-info.json";

	private string _agent;

	private string _appLocation;

	private static readonly HttpClient Client = new HttpClient();

	private const string dokkPkgName = "dokkpackage.zip";

	private string UpdateInfoFile => Path.Combine(ReleasePath, "update-info.json");

	private string ReleasePath => Path.GetDirectoryName(_appLocation);

	private string UpdateUri
	{
		get
		{
			string text = "https://dokkapi.ankerdb.com/api/config/check-update";
			if (!string.IsNullOrWhiteSpace(_agent))
			{
				text = text + "?agent=" + _agent;
			}
			return text;
		}
	}

	public bool IsFirstTimeUpdate => !File.Exists(UpdateInfoFile);

	public Updater(string appLocation, string agent)
	{
		_appLocation = appLocation;
		_agent = agent;
	}

	private Uri DownloadUri()
	{
		string text = "https://dokkapi.ankerdb.com/api/config/download";
		if (!string.IsNullOrWhiteSpace(_agent))
		{
			text = text + "?agent=" + _agent;
		}
		return new Uri(text);
	}

	private Uri StaticDownloadUri(string id)
	{
		string text = "https://dokkapi.ankerdb.com/api/config/" + id + "/download";
		if (!string.IsNullOrWhiteSpace(_agent))
		{
			text = text + "?agent=" + _agent;
		}
		return new Uri(text);
	}

	private UpdateAvailability ReadCurrentInfo()
	{
		if (IsFirstTimeUpdate)
		{
			return null;
		}
		try
		{
			return JsonConvert.DeserializeObject<UpdateAvailability>(File.ReadAllText(UpdateInfoFile));
		}
		catch (Exception)
		{
			return null;
		}
	}

	public void CheckAndDownloadUpdatesBlocking(string clientId)
	{
		if (NetworkInterface.GetIsNetworkAvailable())
		{
			UpdateAvailability updateAvailability = ReadCurrentInfo();
			if (updateAvailability == null)
			{
				updateAvailability = new UpdateAvailability
				{
					ClientID = clientId
				};
				DownloadUpdatesBlocking(updateAvailability);
			}
			else if (IsUpdateAvailableBlocking(updateAvailability))
			{
				DownloadUpdatesBlocking(updateAvailability);
			}
		}
	}

	private bool IsUpdateAvailableBlocking(UpdateAvailability updateAvailability)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Expected O, but got Unknown
		Task<HttpResponseMessage> task = Client.PostAsync(UpdateUri, (HttpContent)new StringContent(JsonConvert.SerializeObject((object)updateAvailability)));
		task.Wait();
		Task<string> task2 = task.Result.Content.ReadAsStringAsync();
		task2.Wait();
		return (JsonConvert.DeserializeObject<DokkResponse>(task2.Result)?.Data)?.IsUpdateAvailable ?? true;
	}

	private void DownloadUpdatesBlocking(UpdateAvailability info)
	{
		string text = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		UpdateAvailability info2;
		try
		{
			info2 = DownloadClientUpdatesBlocking(info, text);
		}
		catch (Exception)
		{
			return;
		}
		try
		{
			UpdateFiles(info2, text);
		}
		catch (Exception)
		{
			return;
		}
		SaveUpdateInfo(info2);
		Directory.Delete(text, recursive: true);
	}

	private UpdateAvailability DownloadClientUpdatesBlocking(UpdateAvailability version, string tempDirectory)
	{
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Expected O, but got Unknown
		Directory.CreateDirectory(tempDirectory);
		string text = Path.Combine(tempDirectory, "package.zip");
		if (version.UpdateInfo == null)
		{
			using WebClient webClient = new WebClient();
			webClient.DownloadFile(StaticDownloadUri(version.ClientID), text);
		}
		else
		{
			Task<HttpResponseMessage> task = Client.PostAsync(DownloadUri(), (HttpContent)new StringContent(JsonConvert.SerializeObject((object)version)));
			task.Wait();
			using FileStream fileStream = new FileStream(text, FileMode.CreateNew);
			task.Result.Content.CopyToAsync((Stream)fileStream).Wait();
		}
		if (!File.Exists(text))
		{
			return null;
		}
		ZipArchive val = ZipFile.Open(text, (ZipArchiveMode)0);
		try
		{
			val.ExtractToDirectory(tempDirectory);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		return JsonConvert.DeserializeObject<UpdateAvailability>(File.ReadAllText(Path.Combine(tempDirectory, "update-info.json")));
	}

	private void UpdateFiles(UpdateAvailability info, string tempDirectory)
	{
		if (info == null || info.UpdateInfo == null)
		{
			return;
		}
		foreach (UpdateInfo item in info.UpdateInfo)
		{
			try
			{
				if (item.UpdateActions == null)
				{
					DoDefaultUpdateAction(tempDirectory, item);
				}
				else
				{
					foreach (UpdateAction updateAction in item.UpdateActions)
					{
						DoUpdateAction(updateAction, tempDirectory, item);
					}
				}
			}
			catch (Exception)
			{
				continue;
			}
			item.InstalledVersion = item.LatestVersion;
		}
	}

	private void DoDefaultUpdateAction(string tempDirectory, UpdateInfo update)
	{
		string text = Path.Combine(tempDirectory, update.App) + ".zip";
		if (!File.Exists(text))
		{
			return;
		}
		string text2 = Path.Combine(tempDirectory, update.App);
		try
		{
			Directory.Delete(text2, recursive: true);
		}
		catch
		{
		}
		if (!File.Exists(text))
		{
			return;
		}
		ZipArchive val = ZipFile.Open(text, (ZipArchiveMode)0);
		try
		{
			val.ExtractToDirectory(text2);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		List<string> list = update.Release?.FileNames;
		if (list == null)
		{
			return;
		}
		if (list.Count == 1 && list[0].ToLower() == "dokkpackage.zip")
		{
			ProcessDokkPackage(text2);
			return;
		}
		foreach (string item in list)
		{
			try
			{
				File.Copy(Path.Combine(text2, item), Path.Combine(ReleasePath, item), overwrite: true);
			}
			catch (Exception)
			{
			}
		}
	}

	private void ProcessDokkPackage(string appUpdates)
	{
		ZipArchive val = ZipFile.Open(Path.Combine(appUpdates, "dokkpackage.zip"), (ZipArchiveMode)0);
		try
		{
			val.ExtractToDirectory(appUpdates);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		string[] array = new string[2] { "net4.8", "net8.0-windows" };
		foreach (string path in array)
		{
			string path2 = Path.Combine(appUpdates, path);
			string text = Path.Combine(ReleasePath, path);
			try
			{
				Directory.CreateDirectory(text);
			}
			catch (Exception)
			{
				break;
			}
			string[] files = Directory.GetFiles(path2);
			foreach (string text2 in files)
			{
				string fileName = Path.GetFileName(text2);
				try
				{
					File.Copy(text2, Path.Combine(text, fileName), overwrite: true);
				}
				catch (Exception)
				{
				}
			}
		}
	}

	private void SaveUpdateInfo(UpdateAvailability info)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		using StreamWriter streamWriter = File.CreateText(UpdateInfoFile);
		new JsonSerializer().Serialize((TextWriter)streamWriter, (object)info);
	}

	private void DoUpdateAction(UpdateAction act, string tempDirectory, UpdateInfo update)
	{
		string text = (string.IsNullOrWhiteSpace(act.Location) ? ReleasePath : act.Location);
		switch (act.Type)
		{
		case ActionType.Copy:
			File.Copy(Path.Combine(tempDirectory, update.App, act.FileName), Path.Combine(text, act.FileName), overwrite: true);
			break;
		case ActionType.Extract:
		{
			ZipArchive val = ZipFile.Open(Path.Combine(tempDirectory, update.App, act.FileName), (ZipArchiveMode)0);
			try
			{
				val.ExtractToDirectory(text);
				break;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		case ActionType.Delete:
			File.Delete(Path.Combine(text, act.FileName));
			break;
		default:
			throw new ArgumentOutOfRangeException();
		case ActionType.Run:
			break;
		}
	}
}
public class VersionInfo
{
	[JsonProperty("app_name")]
	public const string AppName = "Anker";

	[JsonProperty("binaries")]
	public string[] Binaries;

	[JsonProperty("version")]
	public string Version;
}
