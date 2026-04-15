// Copyright 2022-2025 Niantic.
#if UNITY_IOS && UNITY_EDITOR_OSX
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEditor.iOS.Xcode;


public class PostBuildProcess : MonoBehaviour
{
    [PostProcessBuild(2)]
    public static void OnPostProcessBuild(BuildTarget buildTarget, string path)
    {
        if (buildTarget == BuildTarget.iOS)
        {
            BuildForIos(path);
        }
    }

    private static void BuildForIos(string path)
    {
        var plistPath = path + "/Info.plist";
        var plist = new PlistDocument();
        plist.ReadFromString(File.ReadAllText(plistPath));
        var rootDict = plist.root;
        rootDict.SetBoolean("UIFileSharingEnabled", true);
        rootDict.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
        rootDict.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        File.WriteAllText(plistPath, plist.WriteToString());


        string projectPath = PBXProject.GetPBXProjectPath(path);
        PBXProject project = new PBXProject();
        project.ReadFromString(File.ReadAllText(projectPath));
        string frameworkGuid = project.GetUnityFrameworkTargetGuid();

        // XCode 26 requires some Swift compatability flags in the  UnityFramework target
        project.AddBuildProperty(frameworkGuid, "OTHER_LDFLAGS", "-Xlinker -U -Xlinker __swift_FORCE_LOAD_$_swiftCompatibility51 -Xlinker -U -Xlinker __swift_FORCE_LOAD_$_swiftCompatibility56 -Xlinker -U -Xlinker __swift_FORCE_LOAD_$_swiftCompatibilityConcurrency");
        File.WriteAllText(projectPath, project.WriteToString());
    }
}


#endif
