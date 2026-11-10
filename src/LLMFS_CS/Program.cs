// LLMFS folder watcher
//
// Save a short text file into a watched folder and a folder of results appears beside it.
//
//   llmfs                    uses llmfs-config.json beside the exe
//   llmfs <config file>      uses the config file given
//
// Ctrl+C stops it.

using System;
using System.IO;
using System.Threading;

namespace CyborgUnicorn.LLMFS
{
	public static class Program
	{
		public static int Main(string[] arrArguments_a)
		{
			int intResult = 0;
			string strConfigFilename = Path.Combine(AppContext.BaseDirectory, "llmfs-config.json");
			LLMFSConfig objConfig = null;

			if (arrArguments_a.Length > 0)
			{
				strConfigFilename = Path.GetFullPath(arrArguments_a[0]);
			}

			try
			{
				objConfig = LLMFSConfig.load(strConfigFilename);
			}
			catch (Exception objException_a)
			{
				Console.WriteLine("Cannot read " + strConfigFilename + ": " + objException_a.Message);
				intResult = 1;
			}

			if (objConfig != null && objConfig.m_arrFolders.Count == 0)
			{
				Console.WriteLine("No folders to watch: add them under \"folders\" in " + strConfigFilename);
				intResult = 1;
			}
			else if (objConfig != null)
			{
				string strStateFilename = Path.Combine(Path.GetDirectoryName(strConfigFilename), "llmfs-state.json");
				LLMFSService objPlanner = objConfig.getService(objConfig.m_strPlanner);

				LLMFSDispatcher.log("LLMFS folder watcher - config: " + strConfigFilename);

				if (objPlanner == null || !objPlanner.isConfigured())
				{
					LLMFSDispatcher.log("WARNING: the planner '" + objConfig.m_strPlanner + "' is not configured - no request can be planned.");
				}

				CancellationTokenSource objCancel = new CancellationTokenSource();

				Console.CancelKeyPress += delegate(object objSender_a, ConsoleCancelEventArgs objArgs_a)
				{
					objArgs_a.Cancel = true;
					objCancel.Cancel();
				};

				LLMFSEngine objEngine = new LLMFSEngine(objConfig);
				LLMFSDispatcher objDispatcher = new LLMFSDispatcher(objConfig, objEngine, strStateFilename);

				objDispatcher.run(objCancel.Token).GetAwaiter().GetResult();

				LLMFSDispatcher.log("Stopped.");
			}

			return intResult;
		}
	}
}
