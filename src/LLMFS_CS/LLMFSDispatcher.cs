// LLMFS folder watcher - the watcher and dispatcher
//
// Watches the nominated folders and their subfolders.
//
//   A new short text file is a request: its content is what is being asked for.
//   The result is a folder named after the file (peter.txt -> peter), holding one or many files.
//   Each file in it shows its state in its name:
//     name.busy    being made (holds what that file was asked to be)
//     name         finished
//     name.error   failed (holds the reason)
//
//   Files that were already there, and folders the user makes, are never touched.
//   Saving a request again with different content runs it again into a new folder (peter 2).

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace CyborgUnicorn.LLMFS
{
	public class LLMFSRequest
	{
		public LLMFSFolder m_objFolder = null;
		public string m_strPath = "";
		public string m_strText = "";
		public string m_strHash = "";
	}

	public class LLMFSDispatcher
	{
		private const string m_BUSY = ".busy";
		private const string m_ERROR = ".error";
		private const string m_PLANNAME = "_plan";
		private const int m_SETTLEMILLISECONDS = 1000;	// a file has to be quiet this long before it is read
		private const int m_TICKMILLISECONDS = 500;
		private const int m_BATCHMILLISECONDS = 1500;	// requests are held until no new one has turned up for this long, then run as one batch

		private static readonly string[] m_arrImageExtensions = new string[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

		private static readonly string[] m_arrBinaryExtensions = new string[] {
			".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff",
			".mp3", ".wav", ".ogg", ".mp4", ".avi", ".mov",
			".pdf", ".zip", ".docx", ".xlsx", ".pptx", ".exe", ".dll", ".bin"
		};

		private LLMFSConfig m_objConfig;
		private LLMFSEngine m_objEngine;
		private string m_strStateFilename;

		private object m_objLock = new object();
		private HashSet<string> m_objKnown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);						// every file seen so far
		private Dictionary<string, string> m_objRequests = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);	// request file -> hash of the content last run
		private Dictionary<string, long> m_objLastSeen = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);		// watched folder -> when it was last scanned
		private Dictionary<string, DateTime> m_objPending = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);	// file -> when it last changed
		private bool m_blnStateDirty = false;
		private bool m_blnRescanNow = false;

		private List<FileSystemWatcher> m_arrWatchers = new List<FileSystemWatcher>();
		private SemaphoreSlim m_objParallel;

		public LLMFSDispatcher(LLMFSConfig objConfig_a, LLMFSEngine objEngine_a, string strStateFilename_a)
		{
			m_objConfig = objConfig_a;
			m_objEngine = objEngine_a;
			m_strStateFilename = strStateFilename_a;
			m_objParallel = new SemaphoreSlim(objConfig_a.m_intParallel);
		}

		// ---- helpers ----

		public static void log(string strMessage_a)
		{
			Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + strMessage_a);
		}

		private static string hashText(string strText_a)
		{
			using (SHA256 objSHA = SHA256.Create())
			{
				return Convert.ToHexString(objSHA.ComputeHash(Encoding.UTF8.GetBytes(strText_a)));
			}
		}

		private static bool isBinaryExtension(string strFilename_a)
		{
			return Array.IndexOf(m_arrBinaryExtensions, Path.GetExtension(strFilename_a).ToLowerInvariant()) != -1;
		}

		private static bool isImageExtension(string strFilename_a)
		{
			return Array.IndexOf(m_arrImageExtensions, Path.GetExtension(strFilename_a).ToLowerInvariant()) != -1;
		}

		// the watched folder a path sits in (null when it is in none of them)
		private LLMFSFolder findFolder(string strPath_a)
		{
			LLMFSFolder objResult = null;
			int intI;

			for (intI = 0; intI < m_objConfig.m_arrFolders.Count; intI++)
			{
				LLMFSFolder objFolder = m_objConfig.m_arrFolders[intI];

				if (strPath_a.StartsWith(objFolder.m_strPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					if (objResult == null || objFolder.m_strPath.Length > objResult.m_strPath.Length)
					{
						objResult = objFolder;
					}
				}
			}

			return objResult;
		}

		// files that are never requests: our own state files, temp files, hidden and system files
		private static bool isIgnored(string strPath_a)
		{
			string strName = Path.GetFileName(strPath_a);
			string strLower = strName.ToLowerInvariant();
			bool blnResult = false;

			if (strLower.EndsWith(m_BUSY) || strLower.EndsWith(m_ERROR) || strLower.EndsWith(".tmp") ||
			strLower.StartsWith("~") || strLower.StartsWith(".") ||
			strLower == "desktop.ini" || strLower == "thumbs.db")
			{
				blnResult = true;
			}
			else
			{
				try
				{
					FileAttributes intAttributes = File.GetAttributes(strPath_a);
					blnResult = ((intAttributes & (FileAttributes.Hidden | FileAttributes.System)) != 0);
				}
				catch (Exception)
				{
					blnResult = false;
				}
			}

			return blnResult;
		}

		// 0 = not a request (too big, empty or not text), 1 = a request (text in strText_a), 2 = still being written, try later
		private int readRequest(string strPath_a, out string strText_a)
		{
			int intResult = 0;
			strText_a = "";

			try
			{
				FileInfo objInfo = new FileInfo(strPath_a);

				if (objInfo.Exists && objInfo.Length > 0 && objInfo.Length <= m_objConfig.m_intMaxRequestSize)
				{
					byte[] arrBytes;

					// opened with no sharing: fails while the program saving it still has it open
					using (FileStream objStream = new FileStream(strPath_a, FileMode.Open, FileAccess.Read, FileShare.None))
					{
						arrBytes = new byte[objStream.Length];
						int intRead = 0;
						int intGot = 1;

						while (intRead < arrBytes.Length && intGot > 0)
						{
							intGot = objStream.Read(arrBytes, intRead, arrBytes.Length - intRead);
							intRead += intGot;
						}
					}

					string strText = null;

					if (arrBytes.Length >= 2 && arrBytes[0] == 0xFF && arrBytes[1] == 0xFE)
					{
						strText = Encoding.Unicode.GetString(arrBytes, 2, arrBytes.Length - 2);
					}
					else if (arrBytes.Length >= 2 && arrBytes[0] == 0xFE && arrBytes[1] == 0xFF)
					{
						strText = Encoding.BigEndianUnicode.GetString(arrBytes, 2, arrBytes.Length - 2);
					}
					else if (Array.IndexOf(arrBytes, (byte)0) == -1)
					{
						try
						{
							strText = new UTF8Encoding(false, true).GetString(arrBytes);
						}
						catch (ArgumentException)
						{
							strText = Encoding.Latin1.GetString(arrBytes);	// an old ANSI text file
						}
					}

					if (strText != null)
					{
						strText = strText.Trim('﻿', ' ', '\t', '\r', '\n');

						if (strText.Length > 0)
						{
							strText_a = strText;
							intResult = 1;
						}
					}
				}
			}
			catch (IOException)
			{
				intResult = 2;
			}
			catch (UnauthorizedAccessException)
			{
				intResult = 0;
			}

			return intResult;
		}

		// ---- state (kept beside the exe so the watched folders stay clean) ----

		private void loadState()
		{
			try
			{
				if (File.Exists(m_strStateFilename))
				{
					JsonNode objRoot = JsonNode.Parse(File.ReadAllText(m_strStateFilename));

					if (objRoot != null && objRoot["folders"] is JsonObject)
					{
						foreach (KeyValuePair<string, JsonNode> objEntry in objRoot["folders"].AsObject())
						{
							long lngTicks;
							if (objEntry.Value != null && long.TryParse(objEntry.Value.ToString(), out lngTicks))
							{
								m_objLastSeen[objEntry.Key] = lngTicks;
							}
						}
					}

					if (objRoot != null && objRoot["requests"] is JsonObject)
					{
						foreach (KeyValuePair<string, JsonNode> objEntry in objRoot["requests"].AsObject())
						{
							if (objEntry.Value != null)
							{
								m_objRequests[objEntry.Key] = objEntry.Value.ToString();
							}
						}
					}
				}
			}
			catch (Exception objException_a)
			{
				log("State file could not be read (" + objException_a.Message + ") - starting fresh.");
			}
		}

		private void saveState()
		{
			JsonObject objFolders = new JsonObject();
			JsonObject objRequests = new JsonObject();

			lock (m_objLock)
			{
				foreach (KeyValuePair<string, long> objEntry in m_objLastSeen)
				{
					objFolders[objEntry.Key] = objEntry.Value.ToString();
				}

				foreach (KeyValuePair<string, string> objEntry in m_objRequests)
				{
					objRequests[objEntry.Key] = objEntry.Value;
				}

				m_blnStateDirty = false;
			}

			try
			{
				JsonObject objRoot = new JsonObject();
				objRoot["folders"] = objFolders;
				objRoot["requests"] = objRequests;

				JsonSerializerOptions objOptions = new JsonSerializerOptions();
				objOptions.WriteIndented = true;

				File.WriteAllText(m_strStateFilename, objRoot.ToJsonString(objOptions));
			}
			catch (Exception objException_a)
			{
				log("State file could not be written: " + objException_a.Message);
			}
		}

		// ---- scanning ----

		private void enqueue(string strPath_a)
		{
			lock (m_objLock)
			{
				m_objPending[strPath_a] = DateTime.UtcNow;
			}
		}

		// blnStartup_a: files older than the last time this folder was seen were already there - they are not requests
		private void scanFolder(LLMFSFolder objFolder_a, bool blnStartup_a)
		{
			long lngNow = DateTime.UtcNow.Ticks;
			long lngLastSeen = lngNow;
			HashSet<string> objExisting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] arrFiles = new string[0];

			lock (m_objLock)
			{
				if (m_objLastSeen.ContainsKey(objFolder_a.m_strPath))
				{
					lngLastSeen = m_objLastSeen[objFolder_a.m_strPath];
				}
			}

			try
			{
				EnumerationOptions objOptions = new EnumerationOptions();
				objOptions.RecurseSubdirectories = true;
				objOptions.IgnoreInaccessible = true;
				objOptions.AttributesToSkip = 0;

				arrFiles = Directory.GetFiles(objFolder_a.m_strPath, "*", objOptions);
			}
			catch (Exception objException_a)
			{
				log("Could not scan " + objFolder_a.m_strPath + ": " + objException_a.Message);
			}

			// a .busy file still there at startup was being made when the watcher last stopped: nothing will finish it
			if (blnStartup_a)
			{
				int intJ;

				for (intJ = 0; intJ < arrFiles.Length; intJ++)
				{
					if (arrFiles[intJ].EndsWith(m_BUSY, StringComparison.OrdinalIgnoreCase))
					{
						string strWas = "";
						string strStale = arrFiles[intJ].Substring(0, arrFiles[intJ].Length - m_BUSY.Length) + m_ERROR;

						try
						{
							strWas = File.ReadAllText(arrFiles[intJ]).Trim();
						}
						catch (Exception)
						{
							strWas = "";
						}

						failFile(arrFiles[intJ], strStale, "The watcher stopped while this was being made.", strWas);
						arrFiles[intJ] = strStale;
					}
				}
			}

			lock (m_objLock)
			{
				int intI;

				for (intI = 0; intI < arrFiles.Length; intI++)
				{
					string strPath = arrFiles[intI];
					objExisting.Add(strPath);

					if (m_objRequests.ContainsKey(strPath))
					{
						// a request we have run before: look at it again in case its content changed
						m_objKnown.Add(strPath);
						if (blnStartup_a)
						{
							m_objPending[strPath] = DateTime.UtcNow;
						}
					}
					else if (!m_objKnown.Contains(strPath))
					{
						bool blnOld = false;

						if (blnStartup_a)
						{
							try
							{
								FileInfo objInfo = new FileInfo(strPath);
								long lngFileTicks = Math.Max(objInfo.CreationTimeUtc.Ticks, objInfo.LastWriteTimeUtc.Ticks);
								blnOld = (lngFileTicks <= lngLastSeen);
							}
							catch (Exception)
							{
								blnOld = true;
							}
						}

						if (blnOld)
						{
							m_objKnown.Add(strPath);
						}
						else if (!m_objPending.ContainsKey(strPath))
						{
							m_objPending[strPath] = DateTime.UtcNow;
						}
					}
				}

				// forget files that are gone, so the same name saved later is new again
				string strPrefix = objFolder_a.m_strPath + Path.DirectorySeparatorChar;
				List<string> arrGone = new List<string>();

				foreach (string strKnown in m_objKnown)
				{
					if (strKnown.StartsWith(strPrefix, StringComparison.OrdinalIgnoreCase) && !objExisting.Contains(strKnown))
					{
						arrGone.Add(strKnown);
					}
				}

				foreach (string strRequest in m_objRequests.Keys)
				{
					if (strRequest.StartsWith(strPrefix, StringComparison.OrdinalIgnoreCase) && !objExisting.Contains(strRequest) && !arrGone.Contains(strRequest))
					{
						arrGone.Add(strRequest);
					}
				}

				for (intI = 0; intI < arrGone.Count; intI++)
				{
					m_objKnown.Remove(arrGone[intI]);
					if (m_objRequests.Remove(arrGone[intI]))
					{
						m_blnStateDirty = true;
					}
				}

				m_objLastSeen[objFolder_a.m_strPath] = lngNow;
				m_blnStateDirty = true;
			}
		}

		// ---- watcher events ----

		private void onChanged(object objSender_a, FileSystemEventArgs objArgs_a)
		{
			enqueue(objArgs_a.FullPath);
		}

		private void onDeleted(object objSender_a, FileSystemEventArgs objArgs_a)
		{
			lock (m_objLock)
			{
				m_objKnown.Remove(objArgs_a.FullPath);
				m_objPending.Remove(objArgs_a.FullPath);
				if (m_objRequests.Remove(objArgs_a.FullPath))
				{
					m_blnStateDirty = true;
				}
			}
		}

		private void onRenamed(object objSender_a, RenamedEventArgs objArgs_a)
		{
			lock (m_objLock)
			{
				// a file that was already there keeps that standing under its new name
				if (m_objKnown.Remove(objArgs_a.OldFullPath))
				{
					m_objKnown.Add(objArgs_a.FullPath);
				}

				if (m_objRequests.ContainsKey(objArgs_a.OldFullPath))
				{
					m_objRequests[objArgs_a.FullPath] = m_objRequests[objArgs_a.OldFullPath];
					m_objRequests.Remove(objArgs_a.OldFullPath);
					m_blnStateDirty = true;
				}

				m_objPending.Remove(objArgs_a.OldFullPath);
			}

			enqueue(objArgs_a.FullPath);
		}

		private void onError(object objSender_a, ErrorEventArgs objArgs_a)
		{
			// the watcher lost events: the next rescan picks up whatever was missed
			m_blnRescanNow = true;
		}

		// ---- the dispatcher ----

		// looks at the files that have gone quiet and returns the ones that are requests to run
		private List<LLMFSRequest> collectRequests()
		{
			List<LLMFSRequest> arrRequests = new List<LLMFSRequest>();
			List<string> arrDue = new List<string>();
			DateTime dtmNow = DateTime.UtcNow;
			int intI;

			lock (m_objLock)
			{
				foreach (KeyValuePair<string, DateTime> objEntry in m_objPending)
				{
					if ((dtmNow - objEntry.Value).TotalMilliseconds >= m_SETTLEMILLISECONDS)
					{
						arrDue.Add(objEntry.Key);
					}
				}

				for (intI = 0; intI < arrDue.Count; intI++)
				{
					m_objPending.Remove(arrDue[intI]);
				}
			}

			for (intI = 0; intI < arrDue.Count; intI++)
			{
				string strPath = arrDue[intI];
				LLMFSFolder objFolder = findFolder(strPath);

				if (objFolder != null && Directory.Exists(strPath))
				{
					// a folder appeared (made, moved or copied in): folders are never requests, but look inside it
					m_blnRescanNow = true;
				}
				else if (objFolder != null && File.Exists(strPath) && !isIgnored(strPath))
				{
					bool blnKnown;
					string strLastHash = null;

					lock (m_objLock)
					{
						blnKnown = m_objKnown.Contains(strPath);
						if (m_objRequests.ContainsKey(strPath))
						{
							strLastHash = m_objRequests[strPath];
						}
					}

					// a file that was already there and is not one of our requests: a plain save, leave it alone
					if (!blnKnown || strLastHash != null)
					{
						string strText;
						int intRead = readRequest(strPath, out strText);

						if (intRead == 2)
						{
							enqueue(strPath);	// still being written
						}
						else
						{
							lock (m_objLock)
							{
								m_objKnown.Add(strPath);
							}

							if (intRead == 1)
							{
								string strHash = hashText(strText);

								if (strHash != strLastHash)
								{
									LLMFSRequest objRequest = new LLMFSRequest();

									objRequest.m_objFolder = objFolder;
									objRequest.m_strPath = strPath;
									objRequest.m_strText = strText;
									objRequest.m_strHash = strHash;

									arrRequests.Add(objRequest);
								}
							}
						}
					}
				}
			}

			return arrRequests;
		}

		private void rememberRequest(LLMFSRequest objRequest_a)
		{
			lock (m_objLock)
			{
				m_objRequests[objRequest_a.m_strPath] = objRequest_a.m_strHash;
				m_blnStateDirty = true;
			}
		}

		public async Task run(CancellationToken objCancel_a)
		{
			DateTime dtmLastRescan = DateTime.UtcNow;
			DateTime dtmLastRequest = DateTime.UtcNow;
			List<LLMFSRequest> arrBatch = new List<LLMFSRequest>();
			int intI;

			loadState();

			for (intI = 0; intI < m_objConfig.m_arrFolders.Count; intI++)
			{
				LLMFSFolder objFolder = m_objConfig.m_arrFolders[intI];

				try
				{
					Directory.CreateDirectory(objFolder.m_strPath);

					scanFolder(objFolder, true);

					FileSystemWatcher objWatcher = new FileSystemWatcher(objFolder.m_strPath);
					objWatcher.IncludeSubdirectories = true;
					objWatcher.InternalBufferSize = 64 * 1024;
					objWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
					objWatcher.Created += onChanged;
					objWatcher.Changed += onChanged;
					objWatcher.Deleted += onDeleted;
					objWatcher.Renamed += onRenamed;
					objWatcher.Error += onError;
					objWatcher.EnableRaisingEvents = true;

					m_arrWatchers.Add(objWatcher);
					log("Watching " + objFolder.m_strPath);
				}
				catch (Exception objException_a)
				{
					log("Cannot watch " + objFolder.m_strPath + ": " + objException_a.Message);
				}
			}

			saveState();

			while (!objCancel_a.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(m_TICKMILLISECONDS, objCancel_a);
				}
				catch (TaskCanceledException)
				{
				}

				if (!objCancel_a.IsCancellationRequested)
				{
					if (m_blnRescanNow || (DateTime.UtcNow - dtmLastRescan).TotalSeconds >= m_objConfig.m_intRescan)
					{
						m_blnRescanNow = false;
						dtmLastRescan = DateTime.UtcNow;

						for (intI = 0; intI < m_objConfig.m_arrFolders.Count; intI++)
						{
							scanFolder(m_objConfig.m_arrFolders[intI], false);
						}
					}

					// new requests join the batch; the batch runs once things have gone quiet
					List<LLMFSRequest> arrRequests = collectRequests();

					for (intI = 0; intI < arrRequests.Count; intI++)
					{
						LLMFSRequest objRequest = arrRequests[intI];
						int intJ;

						// the same file saved twice in a row: only its latest content is run
						for (intJ = arrBatch.Count - 1; intJ >= 0; intJ--)
						{
							if (string.Equals(arrBatch[intJ].m_strPath, objRequest.m_strPath, StringComparison.OrdinalIgnoreCase))
							{
								arrBatch.RemoveAt(intJ);
							}
						}

						rememberRequest(objRequest);
						arrBatch.Add(objRequest);
						dtmLastRequest = DateTime.UtcNow;
					}

					if (arrBatch.Count > 0 && (DateTime.UtcNow - dtmLastRequest).TotalMilliseconds >= m_BATCHMILLISECONDS)
					{
						// a pile of request files turning up at once (a folder of notes copied in) is confirmed as one question
						bool blnGoAhead = true;

						if (m_objConfig.m_blnConfirm && m_objConfig.m_intBurst > 0 && arrBatch.Count > m_objConfig.m_intBurst)
						{
							blnGoAhead = LLMFSConfirm.ask(arrBatch.Count + " new request files appeared at once, the first is:\n" +
								arrBatch[0].m_strPath + "\n\nRun them all?");
						}

						for (intI = 0; intI < arrBatch.Count; intI++)
						{
							LLMFSRequest objRequest = arrBatch[intI];

							if (blnGoAhead)
							{
								Task objIgnored = Task.Run(() => runJob(objRequest));
							}
							else
							{
								log("Not run: " + objRequest.m_strPath);
							}
						}

						arrBatch.Clear();
					}

					if (m_blnStateDirty)
					{
						saveState();
					}
				}
			}

			for (intI = 0; intI < m_arrWatchers.Count; intI++)
			{
				m_arrWatchers[intI].Dispose();
			}

			saveState();
		}

		// ---- a job: one request file becomes one result folder ----

		// peter.txt -> peter, or peter 2, peter 3... when that name is taken
		private string createResultFolder(string strRequestPath_a)
		{
			string strFolder = Path.GetDirectoryName(strRequestPath_a);
			string strBase = Path.GetFileNameWithoutExtension(strRequestPath_a);
			string strResult = "";
			int intNumber = 1;

			if (strBase.Length == 0)
			{
				strBase = "result";
			}

			lock (m_objLock)
			{
				while (strResult.Length == 0)
				{
					string strCandidate = Path.Combine(strFolder, (intNumber == 1 ? strBase : strBase + " " + intNumber));

					if (!Directory.Exists(strCandidate) && !File.Exists(strCandidate))
					{
						Directory.CreateDirectory(strCandidate);
						strResult = strCandidate;
					}

					intNumber++;
				}
			}

			return strResult;
		}

		// our own files are recorded before they are written, so the watcher never takes one for a request
		private void writeOwnFile(string strPath_a, string strContent_a)
		{
			lock (m_objLock)
			{
				m_objKnown.Add(strPath_a);
			}

			File.WriteAllText(strPath_a, strContent_a, new UTF8Encoding(false));
		}

		private void failFile(string strBusyPath_a, string strErrorPath_a, string strMessage_a, string strAskedFor_a)
		{
			try
			{
				writeOwnFile(strBusyPath_a, "ERROR: " + strMessage_a + "\n\nASKED FOR: " + strAskedFor_a + "\n");

				if (File.Exists(strErrorPath_a))
				{
					File.Delete(strErrorPath_a);
				}
				File.Move(strBusyPath_a, strErrorPath_a);
			}
			catch (Exception objException_a)
			{
				log("Could not write " + strErrorPath_a + ": " + objException_a.Message);
			}
		}

		private async Task runJob(LLMFSRequest objRequest_a)
		{
			try
			{
				string strRequestName = Path.GetFileName(objRequest_a.m_strPath);
				string strFolderPath = Path.GetRelativePath(objRequest_a.m_objFolder.m_strPath, Path.GetDirectoryName(objRequest_a.m_strPath));
				strFolderPath = (strFolderPath == "." ? "" : strFolderPath.Replace('/', '\\'));

				string strResultFolder = createResultFolder(objRequest_a.m_strPath);
				string strPlanBusy = Path.Combine(strResultFolder, m_PLANNAME + m_BUSY);
				string strPlanError = Path.Combine(strResultFolder, m_PLANNAME + m_ERROR);
				int intI;

				log("Request: " + objRequest_a.m_strPath);
				writeOwnFile(strPlanBusy, objRequest_a.m_strText + "\n");

				LLMFSPlan objPlan = await m_objEngine.planFiles(strFolderPath, strRequestName, objRequest_a.m_strText);

				if (objPlan.m_strError != null)
				{
					log("Plan failed: " + strRequestName + " - " + objPlan.m_strError);
					failFile(strPlanBusy, strPlanError, objPlan.m_strError, objRequest_a.m_strText);
				}
				else
				{
					// which service makes each file, and how many files each service gets
					Dictionary<string, int> objCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

					for (intI = 0; intI < objPlan.m_arrFiles.Count; intI++)
					{
						LLMFSPlanFile objFile = objPlan.m_arrFiles[intI];

						objFile.m_strServiceName = m_objConfig.getServiceNameForFile(objRequest_a.m_objFolder, objFile.m_strName);
						objCounts[objFile.m_strServiceName] = (objCounts.ContainsKey(objFile.m_strServiceName) ? objCounts[objFile.m_strServiceName] : 0) + 1;
					}

					// big jobs are confirmed first: only services that set a limit, and only when the job is over it
					string strOver = "";

					foreach (KeyValuePair<string, int> objCount in objCounts)
					{
						LLMFSService objService = m_objConfig.getService(objCount.Key);

						// (a service that is not configured makes nothing, so there is nothing to confirm)
						if (objService != null && objService.isConfigured() && objService.m_intConfirmAbove > 0 && objCount.Value > objService.m_intConfirmAbove)
						{
							strOver += "\n  " + objCount.Value + " on " + objCount.Key;
						}
					}

					bool blnGoAhead = true;

					if (m_objConfig.m_blnConfirm && strOver.Length > 0)
					{
						blnGoAhead = LLMFSConfirm.ask(strRequestName + " wants to make " + objPlan.m_arrFiles.Count + " files:" + strOver + "\n\nGo ahead?");
					}

					if (!blnGoAhead)
					{
						log("Not run: " + strRequestName);
						File.Delete(strPlanBusy);
						Directory.Delete(strResultFolder, false);
					}
					else
					{
						log("Plan: " + strRequestName + " -> " + objPlan.m_arrFiles.Count + " file(s) in " + strResultFolder);

						// every planned file shows up straight away as name.busy, holding what it was asked to be
						for (intI = 0; intI < objPlan.m_arrFiles.Count; intI++)
						{
							string strFinal = Path.Combine(strResultFolder, objPlan.m_arrFiles[intI].m_strName);

							lock (m_objLock)
							{
								m_objKnown.Add(strFinal);
							}

							writeOwnFile(strFinal + m_BUSY, objPlan.m_arrFiles[intI].m_strDescription + "\n");
						}

						File.Delete(strPlanBusy);

						List<Task> arrTasks = new List<Task>();

						for (intI = 0; intI < objPlan.m_arrFiles.Count; intI++)
						{
							arrTasks.Add(makeFile(objRequest_a, strFolderPath, strResultFolder, objPlan.m_arrFiles[intI], objPlan.m_arrFiles));
						}

						await Task.WhenAll(arrTasks);
						log("Done: " + strRequestName);
					}
				}
			}
			catch (Exception objException_a)
			{
				log("Job failed: " + objRequest_a.m_strPath + " - " + objException_a.Message);
			}
		}

		private async Task makeFile(LLMFSRequest objRequest_a, string strFolderPath_a, string strResultFolder_a, LLMFSPlanFile objFile_a, List<LLMFSPlanFile> arrFiles_a)
		{
			string strFinal = Path.Combine(strResultFolder_a, objFile_a.m_strName);
			string strBusy = strFinal + m_BUSY;
			string strError = strFinal + m_ERROR;

			await m_objParallel.WaitAsync();

			try
			{
				LLMFSService objService = m_objConfig.getService(objFile_a.m_strServiceName);
				string strFailure = null;
				string strContent = null;
				byte[] arrImage = null;

				if (objService == null || !objService.isConfigured())
				{
					strFailure = "The provider '" + objFile_a.m_strServiceName + "' is not configured.";
				}
				else if (objService.isImageService() && !isImageExtension(objFile_a.m_strName))
				{
					strFailure = "'" + Path.GetExtension(objFile_a.m_strName) + "' files cannot be made by '" + objService.m_strName + "', which is an image service.";
				}
				else if (objService.isImageService())
				{
					// the picture is of what the plan said this file holds; the request itself when the plan said nothing
					string strImagePrompt = (objFile_a.m_strDescription.Length > 0 ? objFile_a.m_strDescription : objRequest_a.m_strText);
					LLMFSImageResult objImage = await m_objEngine.callImage(objService, strImagePrompt, objFile_a.m_strName);

					if (objImage.m_strError != null)
					{
						strFailure = objImage.m_strError;
					}
					else
					{
						arrImage = objImage.m_arrBytes;

						// the service decides the format in the end: the file is named for what it really is
						string strWanted = Path.GetExtension(strFinal).ToLowerInvariant();
						if (strWanted == ".jpeg") { strWanted = ".jpg"; }

						if (strWanted != objImage.m_strExtension)
						{
							strFinal = Path.ChangeExtension(strFinal, objImage.m_strExtension);
						}
					}
				}
				else if (isBinaryExtension(objFile_a.m_strName))
				{
					strFailure = "'" + Path.GetExtension(objFile_a.m_strName) + "' files cannot be made by '" + objService.m_strName + "', which is a text service.";
				}
				else
				{
					LLMFSCallResult objCall = await m_objEngine.callChatGPT(objService,
						m_objEngine.buildFilePrompt(strFolderPath_a, objRequest_a.m_strText, objFile_a, arrFiles_a), "", objService.m_strTemperature);

					if (objCall.m_strError != null)
					{
						strFailure = objCall.m_strError;
					}
					else if (objCall.m_blnTruncated)
					{
						strFailure = "The file was cut off: it is longer than maxtokens (" +
							(objService.m_strMaxTokens.Length > 0 ? objService.m_strMaxTokens : "2000") + ") allows for '" + objService.m_strName + "'.";
					}
					else
					{
						strContent = LLMFSEngine.stripFences(objCall.m_strResponse);
					}
				}

				if (!File.Exists(strBusy))
				{
					// the user deleted the .busy file (or the whole folder) while it was being made: that cancels it
					log("Cancelled: " + strFinal);
				}
				else if (strFailure != null)
				{
					log("Failed: " + strFinal + " - " + strFailure);
					failFile(strBusy, strError, strFailure, objFile_a.m_strDescription);
				}
				else if (arrImage != null)
				{
					lock (m_objLock)
					{
						m_objKnown.Add(strFinal);
					}

					File.WriteAllBytes(strBusy, arrImage);
					File.Move(strBusy, strFinal, true);
					log("Made: " + strFinal);
				}
				else
				{
					writeOwnFile(strBusy, strContent);
					File.Move(strBusy, strFinal, true);
					log("Made: " + strFinal);
				}
			}
			catch (Exception objException_a)
			{
				log("Failed: " + strFinal + " - " + objException_a.Message);
				failFile(strBusy, strError, objException_a.Message, objFile_a.m_strDescription);
			}
			finally
			{
				m_objParallel.Release();
			}
		}
	}
}
