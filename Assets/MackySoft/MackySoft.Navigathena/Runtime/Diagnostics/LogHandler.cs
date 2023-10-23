using System;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace MackySoft.Navigathena.Diagnostics
{

	[Flags]
	public enum LogLevel
	{
		None = 0,
		All = ~0,
		Info = 1 << 0,
		Warning = 1 << 1,
		Error = 1 << 2
	}

	public sealed class LogHandler : ILogHandler
	{

		const string kLogHeader = "[Navigathena] ";

		public LogLevel LogLevel { get; set; } = LogLevel.All;

		public void LogFormat (LogType logType, UnityObject context, string format, params object[] args)
		{
			if (CanLog(logType))
			{
				Debug.unityLogger.logHandler.LogFormat(logType, context, kLogHeader + format, args);
			}
		}

		public void LogException (Exception exception, UnityObject context)
		{
			if (CanLog(LogType.Exception))
			{
				Debug.unityLogger.LogException(exception, context);
			}
		}

		bool CanLog (LogType logType)
		{
			return logType switch
			{
				LogType.Log or LogType.Assert => (LogLevel & LogLevel.Info) != 0,
				LogType.Warning => (LogLevel & LogLevel.Warning) != 0,
				LogType.Error => (LogLevel & LogLevel.Error) != 0,
				LogType.Exception => true,
				_ => throw new ArgumentOutOfRangeException(nameof(logType)),
			};
		}
	}
}