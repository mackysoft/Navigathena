namespace MackySoft.Navigathena.Samples.Common.Confirmation
{
    public sealed record ConfirmationRoute : Route<bool>
    {
        public ConfirmationRoute (string title, string message, string acceptText, string declineText)
        {
            Title = title;
            Message = message;
            AcceptText = acceptText;
            DeclineText = declineText;
        }

        public string Title
        {
            get;
        }
        public string Message
        {
            get;
        }
        public string AcceptText
        {
            get;
        }
        public string DeclineText
        {
            get;
        }
    }
}
