using UnityEngine;
using UnityEngine.UI;

namespace NianticSpatial.NSDK.AR.Samples
{
    public class SitesListManagerButton : MonoBehaviour
    {
        [SerializeField]
        private Text _detailsText;

        [SerializeField]
        private Button _button;

        public string DetailsText
        {
            get
            {
                return _detailsText.text;
            }
            set
            {
                _detailsText.text = value;
            }
        }

        public Button.ButtonClickedEvent OnClickedEvent
        {
            get
            {
                return _button.onClick;
            }
        }
    }
}
