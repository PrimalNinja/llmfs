// LLMFS folder watcher - configuration, read from llmfs-config.json

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyborgUnicorn.LLMFS
{
	public class LLMFSService
	{
		public string m_strName = "";
		public string m_strAPIKey = "";
		public string m_strEndpoint = "";
		public string m_strFlavour = "";
		public string m_strModel = "";
		public string m_strMaxTokens = "";
		public string m_strTemperature = "";
		public string m_strProxy = "";
		public string m_strJSONBearer = "";
		public int m_intConfirmAbove = 0;
		public JsonObject m_objOptions = new JsonObject();	// extra settings passed straight through to an image service

		// the image flavours: each is one provider's way of asking for a picture
		//   openai-image     POST .../v1/images/generations (OpenAI, and anything that copies it, e.g. ollama)
		//   gemini-image     POST .../models/<model>:generateContent (Google)
		//   stability-image  POST .../v2beta/stable-image/generate/<core|ultra|sd3> (Stability AI)
		public string getFlavour()
		{
			string strResult = m_strFlavour.ToLowerInvariant();

			if (strResult == "image")
			{
				strResult = "openai-image";
			}

			return strResult;
		}

		public bool isImageService()
		{
			return getFlavour().EndsWith("-image");
		}

		// same test as the JS: a service needs an endpoint and a model
		// (stability names the model in the endpoint, so there the model can be left empty)
		public bool isConfigured()
		{
			return (m_strName.Length > 0) && (m_strEndpoint.Length > 0) && (m_strModel.Length > 0 || getFlavour() == "stability-image");
		}
	}

	public class LLMFSFolder
	{
		public string m_strPath = "";
		public Dictionary<string, string> m_objFileTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	public class LLMFSConfig
	{
		public bool m_blnConfirm = true;
		public string m_strPlanner = "";
		public int m_intMaxRequestSize = 2048;
		public int m_intMaxFiles = 100;
		public int m_intBurst = 10;
		public int m_intParallel = 2;
		public int m_intTimeout = 120;
		public int m_intRescan = 30;

		public Dictionary<string, LLMFSService> m_objServices = new Dictionary<string, LLMFSService>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> m_objFileTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<LLMFSFolder> m_arrFolders = new List<LLMFSFolder>();

		private static string getString(JsonNode objNode_a, string strKey_a, string strDefault_a)
		{
			string strResult = strDefault_a;

			if (objNode_a != null && objNode_a[strKey_a] != null)
			{
				strResult = objNode_a[strKey_a].ToString();
			}

			return strResult;
		}

		private static int getInt(JsonNode objNode_a, string strKey_a, int intDefault_a)
		{
			int intResult = intDefault_a;
			int intParsed;

			if (int.TryParse(getString(objNode_a, strKey_a, ""), out intParsed))
			{
				intResult = intParsed;
			}

			return intResult;
		}

		public static bool toBoolean(string str_a)
		{
			string strResult = str_a;
			if (strResult == null) { strResult = "N"; }

			strResult = strResult.ToUpperInvariant();

			return (strResult == "Y" || strResult == "YES" || strResult == "T" || strResult == "TRUE" ||
			strResult == "1" || strResult == "ON" || strResult == "ENABLE" || strResult == "ENABLED");
		}

		private static void readFileTypes(JsonNode objNode_a, Dictionary<string, string> objFileTypes_a)
		{
			if (objNode_a != null && objNode_a is JsonObject)
			{
				foreach (KeyValuePair<string, JsonNode> objEntry in objNode_a.AsObject())
				{
					if (objEntry.Value != null)
					{
						objFileTypes_a[objEntry.Key.Trim()] = objEntry.Value.ToString();
					}
				}
			}
		}

		public static LLMFSConfig load(string strFilename_a)
		{
			LLMFSConfig objConfig = new LLMFSConfig();

			JsonDocumentOptions objOptions = new JsonDocumentOptions();
			objOptions.CommentHandling = JsonCommentHandling.Skip;
			objOptions.AllowTrailingCommas = true;

			JsonNode objRoot = JsonNode.Parse(File.ReadAllText(strFilename_a), null, objOptions);

			objConfig.m_blnConfirm = toBoolean(getString(objRoot, "confirm", "true"));
			objConfig.m_strPlanner = getString(objRoot, "planner", "");
			objConfig.m_intMaxRequestSize = getInt(objRoot, "maxrequestsize", objConfig.m_intMaxRequestSize);
			objConfig.m_intMaxFiles = getInt(objRoot, "maxfiles", objConfig.m_intMaxFiles);
			objConfig.m_intBurst = getInt(objRoot, "burst", objConfig.m_intBurst);
			objConfig.m_intParallel = Math.Max(1, getInt(objRoot, "parallel", objConfig.m_intParallel));
			objConfig.m_intTimeout = getInt(objRoot, "timeout", objConfig.m_intTimeout);
			objConfig.m_intRescan = Math.Max(1, getInt(objRoot, "rescan", objConfig.m_intRescan));

			if (objRoot["services"] != null && objRoot["services"] is JsonObject)
			{
				foreach (KeyValuePair<string, JsonNode> objEntry in objRoot["services"].AsObject())
				{
					LLMFSService objService = new LLMFSService();

					objService.m_strName = objEntry.Key;
					objService.m_strAPIKey = getString(objEntry.Value, "apikey", "");
					objService.m_strEndpoint = getString(objEntry.Value, "endpoint", "");
					objService.m_strFlavour = getString(objEntry.Value, "flavour", "");
					objService.m_strModel = getString(objEntry.Value, "model", "");
					objService.m_strMaxTokens = getString(objEntry.Value, "maxtokens", "");
					objService.m_strTemperature = getString(objEntry.Value, "temperature", "");
					objService.m_strProxy = getString(objEntry.Value, "proxy", "");
					objService.m_strJSONBearer = getString(objEntry.Value, "jsonbearer", "");
					objService.m_intConfirmAbove = getInt(objEntry.Value, "confirmabove", 0);

					if (objEntry.Value != null && objEntry.Value["options"] is JsonObject)
					{
						objService.m_objOptions = objEntry.Value["options"].DeepClone().AsObject();
					}

					objConfig.m_objServices[objEntry.Key] = objService;
				}
			}

			readFileTypes(objRoot["filetypes"], objConfig.m_objFileTypes);

			if (objRoot["folders"] != null && objRoot["folders"] is JsonArray)
			{
				foreach (JsonNode objEntry in objRoot["folders"].AsArray())
				{
					LLMFSFolder objFolder = new LLMFSFolder();

					objFolder.m_strPath = getString(objEntry, "path", "");
					if (objEntry != null)
					{
						readFileTypes(objEntry["filetypes"], objFolder.m_objFileTypes);
					}

					if (objFolder.m_strPath.Length > 0)
					{
						objFolder.m_strPath = Path.GetFullPath(objFolder.m_strPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
						objConfig.m_arrFolders.Add(objFolder);
					}
				}
			}

			return objConfig;
		}

		// null when the name is not in the config at all
		public LLMFSService getService(string strName_a)
		{
			LLMFSService objResult = null;

			if (strName_a != null && m_objServices.ContainsKey(strName_a))
			{
				objResult = m_objServices[strName_a];
			}

			return objResult;
		}

		// the service name for a file: the folder's own filetypes first, then the main ones, "*" last in each
		public string getServiceNameForFile(LLMFSFolder objFolder_a, string strFilename_a)
		{
			string strResult = "";
			string strExtension = Path.GetExtension(strFilename_a);

			if (objFolder_a != null && objFolder_a.m_objFileTypes.ContainsKey(strExtension))
			{
				strResult = objFolder_a.m_objFileTypes[strExtension];
			}
			else if (objFolder_a != null && objFolder_a.m_objFileTypes.ContainsKey("*"))
			{
				strResult = objFolder_a.m_objFileTypes["*"];
			}
			else if (m_objFileTypes.ContainsKey(strExtension))
			{
				strResult = m_objFileTypes[strExtension];
			}
			else if (m_objFileTypes.ContainsKey("*"))
			{
				strResult = m_objFileTypes["*"];
			}

			return strResult;
		}
	}
}
