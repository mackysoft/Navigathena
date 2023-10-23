using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MackySoft.Navigathena.Diagnostics
{
	public static class NavigathenaDebug
	{

		readonly static LogHandler s_LogHandler;
		readonly static Logger s_Logger;

		public static ILogger Logger => s_Logger;

		public static LogLevel LogLevel
		{
			get => s_LogHandler.LogLevel;
			set => s_LogHandler.LogLevel = value;
		}

		static NavigathenaDebug ()
		{
			s_LogHandler = new LogHandler();
			s_Logger = new Logger(s_LogHandler);
		}
	}
}