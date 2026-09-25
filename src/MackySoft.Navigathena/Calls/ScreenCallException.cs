using System;

namespace MackySoft.Navigathena
{
    /// <summary>A call failed; its already committed answer and history are not rolled back by this exception.</summary>
    public sealed class ScreenCallException : Exception
    {
        internal ScreenCallException (Guid callId, ScreenCallFailureStage stage, bool answerCommitted, NavigationState finalSnapshot, Exception cause)
            : base("Screen call failed during " + stage + ": " + cause.Message, cause)
        {
            CallId = callId;
            Stage = stage;
            AnswerCommitted = answerCommitted;
            FinalSnapshot = finalSnapshot;
        }

        public Guid CallId
        {
            get;
        }
        public ScreenCallFailureStage Stage
        {
            get;
        }
        public bool AnswerCommitted
        {
            get;
        }
        public NavigationState FinalSnapshot
        {
            get;
        }
    }
}
