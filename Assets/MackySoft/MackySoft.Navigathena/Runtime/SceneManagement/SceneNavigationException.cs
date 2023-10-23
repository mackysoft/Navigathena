#pragma warning disable UNT0006 // Incorrect message signature
#pragma warning disable UNT0033 // Incorrect message case

using System;

namespace MackySoft.Navigathena.SceneManagement
{
	public sealed class SceneNavigationException : InvalidOperationException
	{
		public SceneNavigationException () { }
		public SceneNavigationException (string message) : base(message) { }
		public SceneNavigationException (string message, Exception innerException) : base(message, innerException) { }
	}
}