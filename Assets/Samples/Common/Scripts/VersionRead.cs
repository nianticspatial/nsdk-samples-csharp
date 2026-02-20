// Copyright 2022-2025 Niantic.
using UnityEngine;
using UnityEngine.UI;


public class VersionRead : MonoBehaviour
{

    public Text uiTextBox;

    private const string SamplesVersion = "4.0.0-b2-26022000";

    void Awake()
    {
        Screen.orientation = ScreenOrientation.Portrait;
    }

    // Start is called before the first frame update
    void Start()
    {

        uiTextBox.text = "SDK: " + NianticSpatial.NSDK.AR.Settings.Metadata.Version +
            "\n" + "Samples: " + SamplesVersion;
    }

}
