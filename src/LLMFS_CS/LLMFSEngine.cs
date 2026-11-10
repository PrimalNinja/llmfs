// LLMFS folder watcher - engine: the LLM calls and the prompts
//
// callChatGPT, extractJSON and toBoolean keep the names and the logic of llmfs.js and llmfs-engine.php.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace CyborgUnicorn.LLMFS
{
	public class LLMFSCallResult
	{
		public string m_strError = null;		// null when the call worked
		public string m_strResponse = null;
		public bool m_blnTruncated = false;		// the LLM stopped because it hit maxtokens
	}

	public class LLMFSImageResult
	{
		public string m_strError = null;		// null when the call worked
		public byte[] m_arrBytes = null;		// the picture
		public string m_strExtension = "";		// what the bytes really are: .png, .jpg, .webp or .gif
	}

	public class LLMFSPlanFile
	{
		public string m_strName = "";
		public string m_strDescription = "";
		public string m_strServiceName = "";
	}

	public class LLMFSPlan
	{
		public string m_strError = null;
		public List<LLMFSPlanFile> m_arrFiles = new List<LLMFSPlanFile>();
	}

	public class LLMFSEngine
	{
		private const string m_FSTEMPERATURE = "0";	// planning: keep the file list as stable as the LLM allows

		private LLMFSConfig m_objConfig;
		private HttpClient m_objHTTP;

		public LLMFSEngine(LLMFSConfig objConfig_a)
		{
			m_objConfig = objConfig_a;
			m_objHTTP = new HttpClient();
			m_objHTTP.Timeout = TimeSpan.FromSeconds(objConfig_a.m_intTimeout);
		}

		// ---- reading what the LLM sends back ----

		// LLMs wrap JSON in ``` fences or add chatter even when told not to: take the outermost {...} or [...]
		public static JsonNode extractJSON(string strText_a)
		{
			JsonNode objResult = null;
			string strText = (strText_a == null ? "" : strText_a).Trim();
			int intObject = strText.IndexOf('{');
			int intArray = strText.IndexOf('[');
			int intStart = -1;
			int intEnd = -1;

			if (intObject != -1 && (intArray == -1 || intObject < intArray))
			{
				intStart = intObject;
				intEnd = strText.LastIndexOf('}');
			}
			else if (intArray != -1)
			{
				intStart = intArray;
				intEnd = strText.LastIndexOf(']');
			}

			if (intStart != -1 && intEnd > intStart)
			{
				try
				{
					objResult = JsonNode.Parse(strText.Substring(intStart, intEnd - intStart + 1));
				}
				catch (JsonException)
				{
					objResult = null;
				}
			}

			return objResult;
		}

		// file content comes back wrapped in a ``` fence often enough: when the whole reply is one fence, unwrap it
		public static string stripFences(string strText_a)
		{
			string strResult = strText_a;
			string strText = strText_a.Trim();

			if (strText.StartsWith("```") && strText.EndsWith("```") && strText.Length >= 6)
			{
				int intFirstLine = strText.IndexOf('\n');
				int intLastFence = strText.LastIndexOf("```");

				if (intFirstLine != -1 && intLastFence > intFirstLine)
				{
					string strInner = strText.Substring(intFirstLine + 1, intLastFence - intFirstLine - 1);

					// only when there is no other fence inside (a document that itself has code blocks is left alone)
					if (strInner.IndexOf("```") == -1)
					{
						strResult = strInner.TrimEnd('\r', '\n') + "\n";
					}
				}
			}

			return strResult;
		}

		private static string getText(JsonNode objNode_a)
		{
			return (objNode_a == null ? null : objNode_a.ToString());
		}

		// ---- the LLM call ----

		public async Task<LLMFSCallResult> callChatGPT(LLMFSService objService_a, string strPrompt_a, string strSessionContext_a, string strTemperature_a)
		{
			LLMFSCallResult objResult = new LLMFSCallResult();

			// defaults
			string strFlavour = objService_a.m_strFlavour;
			if (strFlavour.Length == 0) { strFlavour = "chatgpt"; }
			strFlavour = strFlavour.ToLowerInvariant();

			string strMaxTokens = objService_a.m_strMaxTokens;
			if (strMaxTokens.Length == 0) { strMaxTokens = "2000"; }

			string strTemperature = strTemperature_a;
			if (strTemperature == null || strTemperature.Length == 0) { strTemperature = "0.7"; }

			int intMaxTokens;
			if (!int.TryParse(strMaxTokens, out intMaxTokens)) { intMaxTokens = 2000; }

			double dblTemperature;
			if (!double.TryParse(strTemperature, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dblTemperature)) { dblTemperature = 0.7; }

			string strEndpoint = objService_a.m_strEndpoint;
			if (objService_a.m_strProxy.Length > 0)
			{
				strEndpoint = objService_a.m_strProxy + strEndpoint;
			}

			JsonArray arrMessages = new JsonArray();
			string strSystemMessage = "";

			if (strSessionContext_a != null && strSessionContext_a.Length > 0)
			{
				strSystemMessage = "Context:\n" + strSessionContext_a;
			}

			JsonObject objPayload = new JsonObject();
			Dictionary<string, string> objHeaders = new Dictionary<string, string>();

			if (strFlavour == "claude")
			{
				arrMessages.Add(new JsonObject { ["role"] = "user", ["content"] = strPrompt_a });

				objPayload["model"] = objService_a.m_strModel;
				objPayload["max_tokens"] = intMaxTokens;
				objPayload["temperature"] = dblTemperature;
				objPayload["system"] = strSystemMessage;
				objPayload["messages"] = arrMessages;

				if (LLMFSConfig.toBoolean(objService_a.m_strJSONBearer))
				{
					objPayload["bearer"] = objService_a.m_strAPIKey;
				}

				objHeaders["x-api-key"] = objService_a.m_strAPIKey;
				objHeaders["anthropic-version"] = "2023-06-01";
			}
			else if (strFlavour == "gemini")
			{
				string strFullPrompt = strPrompt_a;
				if (strSystemMessage.Length > 0)
				{
					strFullPrompt = strSystemMessage + "\n\nUser: " + strPrompt_a;
				}

				objPayload["contents"] = new JsonArray
				{
					new JsonObject
					{
						["role"] = "user",
						["parts"] = new JsonArray { new JsonObject { ["text"] = strFullPrompt } }
					}
				};
				objPayload["generationConfig"] = new JsonObject
				{
					["maxOutputTokens"] = intMaxTokens,
					["temperature"] = dblTemperature
				};

				strEndpoint = strEndpoint + "?key=" + objService_a.m_strAPIKey;
			}
			else
			{
				if (strSystemMessage.Length > 0)
				{
					arrMessages.Add(new JsonObject { ["role"] = "system", ["content"] = strSystemMessage });
				}
				arrMessages.Add(new JsonObject { ["role"] = "user", ["content"] = strPrompt_a });

				objPayload["model"] = objService_a.m_strModel;
				objPayload["max_tokens"] = intMaxTokens;
				objPayload["temperature"] = dblTemperature;
				objPayload["messages"] = arrMessages;

				if (LLMFSConfig.toBoolean(objService_a.m_strJSONBearer))
				{
					objPayload["bearer"] = objService_a.m_strAPIKey;
				}

				if (objService_a.m_strAPIKey.Length > 0)
				{
					objHeaders["Authorization"] = "Bearer " + objService_a.m_strAPIKey;
				}
			}

			string strBody = null;
			int intStatus = 0;
			string strHTTPError = "";
			bool blnTimeout = false;

			try
			{
				HttpRequestMessage objRequest = new HttpRequestMessage(HttpMethod.Post, strEndpoint);
				objRequest.Content = new StringContent(objPayload.ToJsonString(), Encoding.UTF8, "application/json");

				foreach (KeyValuePair<string, string> objHeader in objHeaders)
				{
					objRequest.Headers.TryAddWithoutValidation(objHeader.Key, objHeader.Value);
				}

				HttpResponseMessage objResponseMessage = await m_objHTTP.SendAsync(objRequest);
				intStatus = (int)objResponseMessage.StatusCode;
				strBody = await objResponseMessage.Content.ReadAsStringAsync();
			}
			catch (TaskCanceledException)
			{
				blnTimeout = true;
				strHTTPError = "Request timed out";
			}
			catch (Exception objException_a)
			{
				strHTTPError = objException_a.Message;
			}

			JsonNode objResponse = null;
			if (strBody != null)
			{
				try
				{
					objResponse = JsonNode.Parse(strBody);
				}
				catch (JsonException)
				{
					objResponse = null;
				}
			}

			string strNoResponse = "No response from " + objService_a.m_strName;

			try
			{
				if (strHTTPError.Length > 0 || intStatus < 200 || intStatus >= 300)
				{
					string strErrorMessage = objService_a.m_strName + " API error: ";

					if (objResponse is JsonObject && objResponse["error"] is JsonObject && objResponse["error"]["message"] != null)
					{
						strErrorMessage += getText(objResponse["error"]["message"]);
					}
					else if (blnTimeout)
					{
						strErrorMessage += "Request timed out";
					}
					else
					{
						strErrorMessage += "error - " + (intStatus > 0 ? "HTTP " + intStatus : strHTTPError);
					}

					objResult.m_strError = strErrorMessage;
				}
				else if (!(objResponse is JsonObject))
				{
					objResult.m_strError = strNoResponse;
				}
				else if (strFlavour == "claude")
				{
					JsonArray arrContent = objResponse["content"] as JsonArray;

					if (arrContent != null && arrContent.Count > 0 && arrContent[0] is JsonObject && arrContent[0]["text"] != null)
					{
						objResult.m_strResponse = getText(arrContent[0]["text"]);
						objResult.m_blnTruncated = (getText(objResponse["stop_reason"]) == "max_tokens");
					}
					else
					{
						objResult.m_strError = strNoResponse;
					}
				}
				else if (strFlavour == "gemini")
				{
					JsonArray arrCandidates = objResponse["candidates"] as JsonArray;

					if (arrCandidates != null && arrCandidates.Count > 0 && arrCandidates[0] is JsonObject)
					{
						JsonNode objCandidate = arrCandidates[0];
						JsonArray arrParts = (objCandidate["content"] is JsonObject ? objCandidate["content"]["parts"] as JsonArray : null);

						if (arrParts != null && arrParts.Count > 0 && arrParts[0] is JsonObject && arrParts[0]["text"] != null)
						{
							objResult.m_strResponse = getText(arrParts[0]["text"]);
						}
						else if (objCandidate["text"] != null)
						{
							objResult.m_strResponse = getText(objCandidate["text"]);
						}
						else if (objCandidate["message"] is JsonObject && objCandidate["message"]["content"] != null)
						{
							objResult.m_strResponse = getText(objCandidate["message"]["content"]);
						}
						else
						{
							objResult.m_strError = strNoResponse + " - unexpected format";
						}

						objResult.m_blnTruncated = (getText(objCandidate["finishReason"]) == "MAX_TOKENS");
					}
					else
					{
						objResult.m_strError = strNoResponse;
					}
				}
				else
				{
					JsonArray arrChoices = objResponse["choices"] as JsonArray;

					if (arrChoices != null && arrChoices.Count > 0 && arrChoices[0] is JsonObject &&
					arrChoices[0]["message"] is JsonObject && arrChoices[0]["message"]["content"] != null)
					{
						objResult.m_strResponse = getText(arrChoices[0]["message"]["content"]);
						objResult.m_blnTruncated = (getText(arrChoices[0]["finish_reason"]) == "length");
					}
					else
					{
						objResult.m_strError = strNoResponse;
					}
				}
			}
			catch (Exception)
			{
				// a reply shaped in a way none of the above expects
				objResult.m_strError = strNoResponse + " - unexpected format";
				objResult.m_strResponse = null;
			}

			return objResult;
		}

		// ---- the image call ----

		// what a block of bytes really is, going by how it starts ("" when it is not a picture we know)
		public static string detectImageExtension(byte[] arrBytes_a)
		{
			string strResult = "";

			if (arrBytes_a != null && arrBytes_a.Length >= 12)
			{
				if (arrBytes_a[0] == 0x89 && arrBytes_a[1] == 0x50 && arrBytes_a[2] == 0x4E && arrBytes_a[3] == 0x47)
				{
					strResult = ".png";
				}
				else if (arrBytes_a[0] == 0xFF && arrBytes_a[1] == 0xD8)
				{
					strResult = ".jpg";
				}
				else if (arrBytes_a[0] == 0x47 && arrBytes_a[1] == 0x49 && arrBytes_a[2] == 0x46)
				{
					strResult = ".gif";
				}
				else if (arrBytes_a[0] == 0x52 && arrBytes_a[1] == 0x49 && arrBytes_a[2] == 0x46 && arrBytes_a[3] == 0x46 &&
				arrBytes_a[8] == 0x57 && arrBytes_a[9] == 0x45 && arrBytes_a[10] == 0x42 && arrBytes_a[11] == 0x50)
				{
					strResult = ".webp";
				}
			}

			return strResult;
		}

		// base64 from a reply, with or without a "data:image/png;base64," lead-in (null when it is not base64)
		private static byte[] decodeBase64(string strText_a)
		{
			byte[] arrResult = null;
			string strText = (strText_a == null ? "" : strText_a.Trim());
			int intComma = strText.IndexOf(',');

			if (strText.StartsWith("data:") && intComma != -1)
			{
				strText = strText.Substring(intComma + 1);
			}

			try
			{
				if (strText.Length > 0)
				{
					arrResult = Convert.FromBase64String(strText);
				}
			}
			catch (FormatException)
			{
				arrResult = null;
			}

			return arrResult;
		}

		// the error message inside a failed reply, whichever way the provider shapes it ("" when there is none)
		private static string getErrorMessage(string strBody_a)
		{
			string strResult = "";

			try
			{
				JsonNode objResponse = JsonNode.Parse(strBody_a);

				if (objResponse is JsonObject && objResponse["error"] is JsonObject && objResponse["error"]["message"] != null)
				{
					strResult = getText(objResponse["error"]["message"]);
				}
				else if (objResponse is JsonObject && objResponse["errors"] is JsonArray && objResponse["errors"].AsArray().Count > 0)
				{
					strResult = getText(objResponse["errors"].AsArray()[0]);	// stability
				}
				else if (objResponse is JsonObject && objResponse["error"] != null)
				{
					strResult = getText(objResponse["error"]);
				}
				else if (objResponse is JsonObject && objResponse["message"] != null)
				{
					strResult = getText(objResponse["message"]);
				}
			}
			catch (Exception)
			{
				strResult = "";
			}

			return strResult;
		}

		// asks an image service for one picture
		//   strPrompt_a     what the picture is of
		//   strFilename_a   the name it will be saved under (its extension is the format asked for, where the provider lets us ask)
		public async Task<LLMFSImageResult> callImage(LLMFSService objService_a, string strPrompt_a, string strFilename_a)
		{
			LLMFSImageResult objResult = new LLMFSImageResult();
			string strFlavour = objService_a.getFlavour();

			string strEndpoint = objService_a.m_strEndpoint;
			if (objService_a.m_strProxy.Length > 0)
			{
				strEndpoint = objService_a.m_strProxy + strEndpoint;
			}

			string strFormat = System.IO.Path.GetExtension(strFilename_a).TrimStart('.').ToLowerInvariant();
			if (strFormat == "jpg") { strFormat = "jpeg"; }

			HttpRequestMessage objRequest = new HttpRequestMessage(HttpMethod.Post, strEndpoint);
			string strNoImage = "No image from " + objService_a.m_strName;

			if (strFlavour == "openai-image")
			{
				// {"model":"...","prompt":"..."} plus whatever is in "options" (size, quality, output_format...)
				JsonObject objPayload = new JsonObject();

				objPayload["model"] = objService_a.m_strModel;
				objPayload["prompt"] = strPrompt_a;

				foreach (KeyValuePair<string, JsonNode> objOption in objService_a.m_objOptions)
				{
					objPayload[objOption.Key] = (objOption.Value == null ? null : objOption.Value.DeepClone());
				}

				objRequest.Content = new StringContent(objPayload.ToJsonString(), Encoding.UTF8, "application/json");

				if (objService_a.m_strAPIKey.Length > 0)
				{
					objRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + objService_a.m_strAPIKey);
				}
			}
			else if (strFlavour == "gemini-image")
			{
				// the model is in the endpoint; "options" go into generationConfig (imageConfig and so on)
				JsonObject objGenerationConfig = new JsonObject();

				objGenerationConfig["responseModalities"] = new JsonArray { "TEXT", "IMAGE" };

				foreach (KeyValuePair<string, JsonNode> objOption in objService_a.m_objOptions)
				{
					objGenerationConfig[objOption.Key] = (objOption.Value == null ? null : objOption.Value.DeepClone());
				}

				JsonObject objPayload = new JsonObject();

				objPayload["contents"] = new JsonArray
				{
					new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = strPrompt_a } } }
				};
				objPayload["generationConfig"] = objGenerationConfig;

				objRequest.Content = new StringContent(objPayload.ToJsonString(), Encoding.UTF8, "application/json");
				objRequest.Headers.TryAddWithoutValidation("x-goog-api-key", objService_a.m_strAPIKey);
			}
			else if (strFlavour == "stability-image")
			{
				// a form, not JSON; the reply is the picture itself
				MultipartFormDataContent objForm = new MultipartFormDataContent();
				bool blnFormatGiven = false;

				objForm.Add(new StringContent(strPrompt_a), "\"prompt\"");

				if (objService_a.m_strModel.Length > 0)
				{
					objForm.Add(new StringContent(objService_a.m_strModel), "\"model\"");
				}

				foreach (KeyValuePair<string, JsonNode> objOption in objService_a.m_objOptions)
				{
					if (objOption.Value != null)
					{
						objForm.Add(new StringContent(objOption.Value.ToString()), "\"" + objOption.Key + "\"");
						blnFormatGiven = blnFormatGiven || (objOption.Key == "output_format");
					}
				}

				if (!blnFormatGiven && (strFormat == "png" || strFormat == "jpeg" || strFormat == "webp"))
				{
					objForm.Add(new StringContent(strFormat), "\"output_format\"");
				}

				// .NET puts quotes round the boundary, which some servers refuse
				foreach (NameValueHeaderValue objParameter in objForm.Headers.ContentType.Parameters)
				{
					if (objParameter.Name == "boundary")
					{
						objParameter.Value = objParameter.Value.Trim('"');
					}
				}

				objRequest.Content = objForm;
				objRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + objService_a.m_strAPIKey);
				objRequest.Headers.TryAddWithoutValidation("Accept", "image/*");
			}
			else
			{
				objResult.m_strError = "'" + objService_a.m_strFlavour + "' is not an image flavour (use openai-image, gemini-image or stability-image).";
			}

			if (objResult.m_strError == null)
			{
				try
				{
					HttpResponseMessage objResponseMessage = await m_objHTTP.SendAsync(objRequest);
					int intStatus = (int)objResponseMessage.StatusCode;
					byte[] arrBody = await objResponseMessage.Content.ReadAsByteArrayAsync();

					if (intStatus < 200 || intStatus >= 300)
					{
						string strMessage = getErrorMessage(Encoding.UTF8.GetString(arrBody));

						objResult.m_strError = objService_a.m_strName + " API error: " + (strMessage.Length > 0 ? strMessage : "error - HTTP " + intStatus);
					}
					else if (detectImageExtension(arrBody).Length > 0)
					{
						// the reply is the picture itself (stability)
						objResult.m_arrBytes = arrBody;
					}
					else
					{
						JsonNode objResponse = JsonNode.Parse(Encoding.UTF8.GetString(arrBody));
						string strURL = null;

						if (objResponse is JsonObject && objResponse["data"] is JsonArray && objResponse["data"].AsArray().Count > 0 && objResponse["data"].AsArray()[0] is JsonObject)
						{
							// openai: data[0].b64_json, or data[0].url to fetch
							JsonNode objData = objResponse["data"].AsArray()[0];

							if (objData["b64_json"] != null)
							{
								objResult.m_arrBytes = decodeBase64(getText(objData["b64_json"]));
							}
							else if (objData["url"] != null)
							{
								strURL = getText(objData["url"]);
							}
						}
						else if (objResponse is JsonObject && objResponse["candidates"] is JsonArray)
						{
							// gemini: the first part of the first candidate that carries inlineData and is not a 'thought'
							JsonArray arrCandidates = objResponse["candidates"].AsArray();
							JsonArray arrParts = null;
							int intI;

							if (arrCandidates.Count > 0 && arrCandidates[0] is JsonObject && arrCandidates[0]["content"] is JsonObject)
							{
								arrParts = arrCandidates[0]["content"]["parts"] as JsonArray;
							}

							for (intI = 0; arrParts != null && intI < arrParts.Count && objResult.m_arrBytes == null; intI++)
							{
								if (arrParts[intI] is JsonObject)
								{
									JsonNode objInline = arrParts[intI]["inlineData"];
									if (objInline == null) { objInline = arrParts[intI]["inline_data"]; }

									bool blnThought = (arrParts[intI]["thought"] != null && getText(arrParts[intI]["thought"]).ToLowerInvariant() == "true");

									if (objInline is JsonObject && objInline["data"] != null && !blnThought)
									{
										objResult.m_arrBytes = decodeBase64(getText(objInline["data"]));
									}
								}
							}
						}
						else if (objResponse is JsonObject && objResponse["image"] != null)
						{
							// stability when it was asked for JSON
							objResult.m_arrBytes = decodeBase64(getText(objResponse["image"]));
						}

						if (strURL != null)
						{
							if (strURL.StartsWith("data:"))
							{
								objResult.m_arrBytes = decodeBase64(strURL);
							}
							else
							{
								objResult.m_arrBytes = await m_objHTTP.GetByteArrayAsync(strURL);
							}
						}
					}
				}
				catch (TaskCanceledException)
				{
					objResult.m_strError = objService_a.m_strName + " API error: Request timed out";
				}
				catch (Exception objException_a)
				{
					objResult.m_strError = objService_a.m_strName + " API error: error - " + objException_a.Message;
				}

				if (objResult.m_strError == null)
				{
					objResult.m_strExtension = detectImageExtension(objResult.m_arrBytes);

					if (objResult.m_strExtension.Length == 0)
					{
						objResult.m_arrBytes = null;
						objResult.m_strError = strNoImage;
					}
				}
			}

			return objResult;
		}

		// ---- the prompts ----

		public string buildPlanPrompt(string strFolderPath_a, string strRequestName_a, string strRequest_a)
		{
			string strPrompt = "You are acting as a filesystem. A user saved a request file and you decide which files to create for it.\n";

			strPrompt += "Folder path: " + (strFolderPath_a.Length > 0 ? strFolderPath_a : "(the top folder)") + "\n";
			strPrompt += "Each part of the folder path is a topic drilled down from the part before it.\n";
			strPrompt += "Request file name: " + strRequestName_a + "\n";
			strPrompt += "Request:\n" + strRequest_a + "\n\n";
			strPrompt += "Plan the files that fulfil the request.\n";
			strPrompt += "- A request for one thing is one file. A request for several things is one file for each.\n";
			strPrompt += "- At most " + m_objConfig.m_intMaxFiles + " files.\n";
			strPrompt += "- Give each file a name with the right extension for what it holds (.txt, .md, .js, .html, .csv, .jpg and so on).\n";
			strPrompt += "- File names: lowercase, no spaces, no folders, each one different.\n";
			strPrompt += "- Give each file a one-line description of exactly what that file must contain.\n";
			strPrompt += "Reply with JSON only, no other text: {\"files\":[{\"name\":\"\",\"description\":\"\"}]}";

			return strPrompt;
		}

		public string buildFilePrompt(string strFolderPath_a, string strRequest_a, LLMFSPlanFile objFile_a, List<LLMFSPlanFile> arrFiles_a)
		{
			string strPrompt = "You are acting as a filesystem. Write the complete content of one file.\n";
			int intI;

			strPrompt += "Folder path: " + (strFolderPath_a.Length > 0 ? strFolderPath_a : "(the top folder)") + "\n";
			strPrompt += "The request the user saved:\n" + strRequest_a + "\n\n";

			if (arrFiles_a.Count > 1)
			{
				strPrompt += "The request is being fulfilled as these files:\n";
				for (intI = 0; intI < arrFiles_a.Count; intI++)
				{
					strPrompt += "- " + arrFiles_a[intI].m_strName + ": " + arrFiles_a[intI].m_strDescription + "\n";
				}
				strPrompt += "\n";
			}

			strPrompt += "Write this file: " + objFile_a.m_strName + "\n";
			strPrompt += "It must contain: " + objFile_a.m_strDescription + "\n";
			strPrompt += "Reply with the content of the file only: no explanation before or after it, no markdown code fence around it.";

			return strPrompt;
		}

		// ---- planning: the request becomes a list of files to make ----

		// a name the LLM gave becomes a safe filename: no folders, no characters Windows refuses
		private static string cleanFilename(string strName_a)
		{
			string strName = strName_a.Trim().Replace('\\', '/');
			int intSlash = strName.LastIndexOf('/');
			StringBuilder objBuilder = new StringBuilder();
			string strInvalid = "<>:\"/\\|?*";
			int intI;

			if (intSlash != -1)
			{
				strName = strName.Substring(intSlash + 1);
			}

			for (intI = 0; intI < strName.Length; intI++)
			{
				char chrCharacter = strName[intI];

				if (chrCharacter < ' ' || strInvalid.IndexOf(chrCharacter) != -1)
				{
					objBuilder.Append('_');
				}
				else
				{
					objBuilder.Append(chrCharacter);
				}
			}

			return objBuilder.ToString().Trim().TrimEnd('.').ToLowerInvariant();
		}

		public async Task<LLMFSPlan> planFiles(string strFolderPath_a, string strRequestName_a, string strRequest_a)
		{
			LLMFSPlan objPlan = new LLMFSPlan();
			LLMFSService objPlanner = m_objConfig.getService(m_objConfig.m_strPlanner);

			if (objPlanner == null || !objPlanner.isConfigured())
			{
				objPlan.m_strError = "The provider '" + m_objConfig.m_strPlanner + "' is not configured.";
			}
			else
			{
				LLMFSCallResult objCall = await callChatGPT(objPlanner, buildPlanPrompt(strFolderPath_a, strRequestName_a, strRequest_a), "", m_FSTEMPERATURE);

				if (objCall.m_strError != null)
				{
					objPlan.m_strError = objCall.m_strError;
				}
				else
				{
					JsonNode objData = extractJSON(objCall.m_strResponse);
					JsonArray arrFiles = null;

					if (objData is JsonObject && objData["files"] is JsonArray)
					{
						arrFiles = objData["files"].AsArray();
					}
					else if (objData is JsonArray)
					{
						arrFiles = objData.AsArray();	// a bare array of files
					}

					if (arrFiles == null)
					{
						objPlan.m_strError = "The plan could not be read.";
					}
					else
					{
						HashSet<string> objSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						int intI;

						for (intI = 0; intI < arrFiles.Count && objPlan.m_arrFiles.Count < m_objConfig.m_intMaxFiles; intI++)
						{
							if (arrFiles[intI] is JsonObject && arrFiles[intI]["name"] != null)
							{
								string strName = cleanFilename(getText(arrFiles[intI]["name"]));

								if (strName.Length > 0 && !objSeen.Contains(strName))
								{
									LLMFSPlanFile objFile = new LLMFSPlanFile();

									objFile.m_strName = strName;
									objFile.m_strDescription = (arrFiles[intI]["description"] == null ? "" : getText(arrFiles[intI]["description"]).Trim());

									objSeen.Add(strName);
									objPlan.m_arrFiles.Add(objFile);
								}
							}
						}

						if (objPlan.m_arrFiles.Count == 0)
						{
							objPlan.m_strError = "The plan has no files in it.";
						}
					}
				}
			}

			return objPlan;
		}
	}
}
