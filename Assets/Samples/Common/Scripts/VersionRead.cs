// Copyright 2022-2025 Niantic.
using UnityEngine;
using UnityEngine.UI;


public class VersionRead : MonoBehaviour
{

    public Text uiTextBox;

    private const string SamplesVersion = "4.0.1-26042802";

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
