// LLMFS folder watcher - asking the user before a big job runs
//
// On Windows this is a plain Yes/No message box (user32, no WinForms needed).
// Anywhere else it asks on the console.

using System;
using System.Runtime.InteropServices;

namespace CyborgUnicorn.LLMFS
{
	public static class LLMFSConfirm
	{
		private const uint MB_YESNO = 0x00000004;
		private const uint MB_ICONQUESTION = 0x00000020;
		private const uint MB_SETFOREGROUND = 0x00010000;
		private const uint MB_TOPMOST = 0x00040000;
		private const int IDYES = 6;

		private static object m_objLock = new object();	// one question on screen at a time

		[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
		private static extern int MessageBox(IntPtr hWnd_a, string strText_a, string strCaption_a, uint intType_a);

		public static bool ask(string strMessage_a)
		{
			bool blnResult = false;

			lock (m_objLock)
			{
				if (OperatingSystem.IsWindows())
				{
					blnResult = (MessageBox(IntPtr.Zero, strMessage_a, "LLMFS", MB_YESNO | MB_ICONQUESTION | MB_SETFOREGROUND | MB_TOPMOST) == IDYES);
				}
				else
				{
					Console.WriteLine(strMessage_a);
					Console.Write("[y/n] ");

					string strAnswer = Console.ReadLine();
					blnResult = (strAnswer != null && strAnswer.Trim().ToLowerInvariant().StartsWith("y"));
				}
			}

			return blnResult;
		}
	}
}
