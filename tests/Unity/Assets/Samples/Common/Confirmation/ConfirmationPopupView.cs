using UnityEngine;
using UnityEngine.UI;

namespace MackySoft.Navigathena.Samples.Common.Confirmation
{
    public sealed class ConfirmationPopupView : MonoBehaviour
    {
        [SerializeField] private Text title = null!;
        [SerializeField] private Text message = null!;
        [SerializeField] private Text acceptText = null!;
        [SerializeField] private Text declineText = null!;
        [SerializeField] private Button accept = null!;
        [SerializeField] private Button decline = null!;
        [SerializeField] private Button close = null!;

        public Button Accept => accept;
        public Button Decline => decline;
        public Button Close => close;

        public void Render (ConfirmationRoute route)
        {
            title.text = route.Title;
            message.text = route.Message;
            acceptText.text = route.AcceptText;
            declineText.text = route.DeclineText;
        }
    }
}
