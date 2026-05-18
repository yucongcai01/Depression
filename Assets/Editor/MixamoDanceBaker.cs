#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class MixamoDanceBaker : EditorWindow
{
    private const string DefaultOutputFolder = "Assets/Resources/DanceClips";

    private static readonly int[] DefaultPlaybackIndices =
    {
        0, 7, 8,
        11, 12, 13, 14, 15, 16,
        23, 24, 25, 26, 27, 28
    };

    private static readonly HumanBodyBones[] RequiredHumanoidBones =
    {
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightFoot
    };

    [SerializeField] private GameObject sourceAvatar;
    [SerializeField] private AnimationClip animationClip;
    [SerializeField] private string outputFolder = DefaultOutputFolder;
    [SerializeField] private string outputName;

    [SerializeField] private float sampleRate = 30f;
    [SerializeField] private bool recordAllMediaPipeLandmarks;
    [SerializeField] private bool ignoreRootMotion = true;
    [SerializeField] private Vector3 bakedAvatarRotation = new Vector3(0f, 180f, 0f);

    [SerializeField] private bool useSceneCameraProjection = true;
    [SerializeField] private Camera projectionCamera;
    [SerializeField] private float cameraDepth = 5f;
    [SerializeField] private float worldScale = 1f;
    [SerializeField] private float fallbackViewportScale = 0.22f;
    [SerializeField] private float fallbackDepth = 5f;
    [SerializeField] private float fallbackDepthScale = 1f;

    private Vector2 scroll;

    [MenuItem("Tools/Dance/Mixamo Dance Baker")]
    public static void OpenWindow()
    {
        MixamoDanceBaker window = GetWindow<MixamoDanceBaker>("Mixamo Dance Baker");
        window.minSize = new Vector2(420f, 560f);
        window.ApplySelection();
    }

    [MenuItem("Assets/Bake Mixamo Dance Clip", true)]
    private static bool CanBakeSelection()
    {
        return Selection.objects.Any(IsBakeableSelectionObject);
    }

    [MenuItem("Assets/Bake Mixamo Dance Clip")]
    private static void BakeSelection()
    {
        MixamoDanceBaker window = GetWindow<MixamoDanceBaker>("Mixamo Dance Baker");
        window.minSize = new Vector2(420f, 560f);
        window.ApplySelection();
        window.Show();
    }

    private void OnEnable()
    {
        if (projectionCamera == null)
            projectionCamera = Camera.main;

        if (string.IsNullOrWhiteSpace(outputFolder))
            outputFolder = DefaultOutputFolder;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "Bake a Mixamo humanoid FBX/AnimationClip into DanceClipData for the existing DancePlayback component.",
            MessageType.Info);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        sourceAvatar = (GameObject)EditorGUILayout.ObjectField("Humanoid FBX / Avatar", sourceAvatar, typeof(GameObject), true);
        animationClip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", animationClip, typeof(AnimationClip), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Selection"))
                ApplySelection();

            if (GUILayout.Button("Find Clip In Avatar"))
                animationClip = FindFirstUsableClip(sourceAvatar) ?? animationClip;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Bake Settings", EditorStyles.boldLabel);
        sampleRate = Mathf.Clamp(EditorGUILayout.FloatField("Sample Rate", sampleRate), 1f, 120f);
        recordAllMediaPipeLandmarks = EditorGUILayout.Toggle("Record All 33 Landmarks", recordAllMediaPipeLandmarks);
        ignoreRootMotion = EditorGUILayout.Toggle("Ignore Root Motion", ignoreRootMotion);
        bakedAvatarRotation = EditorGUILayout.Vector3Field("Baked Avatar Rotation", bakedAvatarRotation);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Coordinate Encoding", EditorStyles.boldLabel);
        useSceneCameraProjection = EditorGUILayout.Toggle("Use Scene Camera Projection", useSceneCameraProjection);
        using (new EditorGUI.DisabledScope(!useSceneCameraProjection))
        {
            projectionCamera = (Camera)EditorGUILayout.ObjectField("Projection Camera", projectionCamera, typeof(Camera), true);
            cameraDepth = Mathf.Max(0.1f, EditorGUILayout.FloatField("Camera Depth", cameraDepth));
            worldScale = Mathf.Max(0.001f, EditorGUILayout.FloatField("World Scale", worldScale));
        }

        using (new EditorGUI.DisabledScope(useSceneCameraProjection && projectionCamera != null))
        {
            fallbackViewportScale = Mathf.Max(0.001f, EditorGUILayout.FloatField("Fallback Viewport Scale", fallbackViewportScale));
            fallbackDepth = EditorGUILayout.FloatField("Fallback Depth", fallbackDepth);
            fallbackDepthScale = EditorGUILayout.FloatField("Fallback Depth Scale", fallbackDepthScale);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        string shownOutputName = GetEffectiveOutputName();
        EditorGUI.BeginChangeCheck();
        shownOutputName = EditorGUILayout.TextField("Asset Name", shownOutputName);
        if (EditorGUI.EndChangeCheck())
            outputName = shownOutputName;

        using (new EditorGUILayout.HorizontalScope())
        {
            outputFolder = EditorGUILayout.TextField("Folder", outputFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(70f)))
                BrowseOutputFolder();
        }

        EditorGUILayout.Space(12f);
        using (new EditorGUI.DisabledScope(!CanBake()))
        {
            if (GUILayout.Button("Bake DanceClipData", GUILayout.Height(34f)))
                BakeWithDialog();
        }

        EditorGUILayout.Space(8f);
        DrawStatus();

        EditorGUILayout.EndScrollView();
    }

    private void DrawStatus()
    {
        if (sourceAvatar == null)
        {
            EditorGUILayout.HelpBox("Choose the imported Mixamo FBX/model prefab, or select it in Project and press Use Selection.", MessageType.None);
            return;
        }

        if (animationClip == null)
        {
            EditorGUILayout.HelpBox("Choose the dance AnimationClip. For Mixamo FBX assets, Find Clip In Avatar usually picks the embedded clip.", MessageType.Warning);
            return;
        }

        if (useSceneCameraProjection && projectionCamera == null)
        {
            EditorGUILayout.HelpBox("No projection camera is assigned. The baker will use fallback viewport/depth encoding.", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox("Ready. The generated asset can be assigned directly to DancePlayback.clip.", MessageType.None);
    }

    private bool CanBake()
    {
        return sourceAvatar != null && animationClip != null && sampleRate >= 1f && !string.IsNullOrWhiteSpace(outputFolder);
    }

    private void BakeWithDialog()
    {
        try
        {
            DanceClipData bakedClip = Bake();
            Selection.activeObject = bakedClip;
            EditorGUIUtility.PingObject(bakedClip);
            Debug.Log($"MixamoDanceBaker: Baked {bakedClip.frames.Count} frames to {AssetDatabase.GetAssetPath(bakedClip)}.");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Mixamo Dance Baker", ex.Message, "OK");
        }
    }

    private DanceClipData Bake()
    {
        EnsureAssetFolder(outputFolder);

        string fileName = SanitizeFileName(GetEffectiveOutputName());
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder.TrimEnd('/')}/{fileName}.asset");

        GameObject instance = null;
        bool startedAnimationMode = false;

        try
        {
            instance = InstantiateSourceAvatar();
            Animator animator = instance.GetComponentInChildren<Animator>();
            ValidateAnimator(animator);

            int[] recordedIndices = recordAllMediaPipeLandmarks
                ? Enumerable.Range(0, 33).ToArray()
                : DefaultPlaybackIndices.ToArray();

            int frameCount = Mathf.Max(1, Mathf.FloorToInt(animationClip.length * sampleRate) + 1);
            List<FrameData> frames = new List<FrameData>(frameCount);

            AnimationMode.StartAnimationMode();
            startedAnimationMode = true;

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float time = frameIndex == frameCount - 1
                    ? animationClip.length
                    : Mathf.Min(frameIndex / sampleRate, animationClip.length);

                EditorUtility.DisplayProgressBar(
                    "Mixamo Dance Baker",
                    $"Sampling {animationClip.name} ({frameIndex + 1}/{frameCount})",
                    frameCount <= 1 ? 1f : frameIndex / (float)(frameCount - 1));

                SampleClip(instance, time);

                if (ignoreRootMotion)
                    ResetSampleRoot(instance);

                Vector3[] landmarks = BuildMediaPipeLandmarks(animator);
                Vector3 hipCenter = (landmarks[23] + landmarks[24]) * 0.5f;

                FrameData frame = new FrameData
                {
                    time = time,
                    landmarkPositions = new Vector3[recordedIndices.Length]
                };

                for (int i = 0; i < recordedIndices.Length; i++)
                {
                    int landmarkIndex = recordedIndices[i];
                    frame.landmarkPositions[i] = EncodeLandmark(landmarks[landmarkIndex], hipCenter);
                }

                frames.Add(frame);
            }

            DanceClipData data = ScriptableObject.CreateInstance<DanceClipData>();
            data.Duration = animationClip.length;
            data.recordedIndices = recordedIndices;
            data.frames = frames;

            AssetDatabase.CreateAsset(data, assetPath);
            AssetDatabase.SaveAssets();
            return data;
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            if (startedAnimationMode)
                AnimationMode.StopAnimationMode();

            if (instance != null)
                DestroyImmediate(instance);
        }
    }

    private GameObject InstantiateSourceAvatar()
    {
        GameObject instance = null;
        string assetPath = AssetDatabase.GetAssetPath(sourceAvatar);

        if (!string.IsNullOrEmpty(assetPath))
            instance = PrefabUtility.InstantiatePrefab(sourceAvatar) as GameObject;

        if (instance == null)
            instance = Instantiate(sourceAvatar);

        instance.name = $"{sourceAvatar.name}_DanceBakeTemp";
        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.SetActive(true);
        ResetSampleRoot(instance);

        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            child.gameObject.hideFlags = HideFlags.HideAndDontSave;

        return instance;
    }

    private void ResetSampleRoot(GameObject instance)
    {
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(bakedAvatarRotation));
        instance.transform.localScale = Vector3.one;
    }

    private void SampleClip(GameObject instance, float time)
    {
        AnimationMode.BeginSampling();
        try
        {
            AnimationMode.SampleAnimationClip(instance, animationClip, Mathf.Clamp(time, 0f, animationClip.length));
        }
        finally
        {
            AnimationMode.EndSampling();
        }
    }

    private static void ValidateAnimator(Animator animator)
    {
        if (animator == null)
            throw new InvalidOperationException("The selected source avatar does not contain an Animator component.");

        if (animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidOperationException("The selected source avatar must use a Humanoid Avatar. Set the Mixamo FBX Rig > Animation Type to Humanoid.");

        List<string> missingBones = new List<string>();
        foreach (HumanBodyBones bone in RequiredHumanoidBones)
        {
            if (animator.GetBoneTransform(bone) == null)
                missingBones.Add(bone.ToString());
        }

        if (missingBones.Count > 0)
            throw new InvalidOperationException($"The humanoid avatar is missing required bones: {string.Join(", ", missingBones)}.");
    }

    private Vector3[] BuildMediaPipeLandmarks(Animator animator)
    {
        Vector3[] lm = new Vector3[33];

        Vector3 leftShoulder = BoneOr(animator, HumanBodyBones.LeftUpperArm, animator.transform.position + animator.transform.TransformVector(new Vector3(-0.2f, 1.4f, 0f)));
        Vector3 rightShoulder = BoneOr(animator, HumanBodyBones.RightUpperArm, animator.transform.position + animator.transform.TransformVector(new Vector3(0.2f, 1.4f, 0f)));
        Vector3 leftElbow = BoneOr(animator, HumanBodyBones.LeftLowerArm, leftShoulder + animator.transform.TransformVector(new Vector3(-0.3f, -0.3f, 0f)));
        Vector3 rightElbow = BoneOr(animator, HumanBodyBones.RightLowerArm, rightShoulder + animator.transform.TransformVector(new Vector3(0.3f, -0.3f, 0f)));
        Vector3 leftWrist = BoneOr(animator, HumanBodyBones.LeftHand, leftElbow + animator.transform.TransformVector(new Vector3(-0.25f, -0.25f, 0f)));
        Vector3 rightWrist = BoneOr(animator, HumanBodyBones.RightHand, rightElbow + animator.transform.TransformVector(new Vector3(0.25f, -0.25f, 0f)));

        Vector3 leftHip = BoneOr(animator, HumanBodyBones.LeftUpperLeg, animator.transform.position + animator.transform.TransformVector(new Vector3(-0.15f, 0.9f, 0f)));
        Vector3 rightHip = BoneOr(animator, HumanBodyBones.RightUpperLeg, animator.transform.position + animator.transform.TransformVector(new Vector3(0.15f, 0.9f, 0f)));
        Vector3 leftKnee = BoneOr(animator, HumanBodyBones.LeftLowerLeg, leftHip + animator.transform.TransformVector(new Vector3(0f, -0.45f, 0f)));
        Vector3 rightKnee = BoneOr(animator, HumanBodyBones.RightLowerLeg, rightHip + animator.transform.TransformVector(new Vector3(0f, -0.45f, 0f)));
        Vector3 leftAnkle = BoneOr(animator, HumanBodyBones.LeftFoot, leftKnee + animator.transform.TransformVector(new Vector3(0f, -0.45f, 0f)));
        Vector3 rightAnkle = BoneOr(animator, HumanBodyBones.RightFoot, rightKnee + animator.transform.TransformVector(new Vector3(0f, -0.45f, 0f)));

        Vector3 hipCenter = (leftHip + rightHip) * 0.5f;
        Vector3 shoulderCenter = (leftShoulder + rightShoulder) * 0.5f;
        Vector3 bodyRight = SafeDirection(rightShoulder - leftShoulder, animator.transform.right);
        Vector3 bodyUp = SafeDirection(shoulderCenter - hipCenter, animator.transform.up);
        Vector3 bodyForward = Vector3.Cross(bodyRight, bodyUp);

        if (bodyForward.sqrMagnitude < 0.0001f)
            bodyForward = animator.transform.forward;
        else
            bodyForward.Normalize();

        if (Vector3.Dot(bodyForward, animator.transform.forward) < 0f)
            bodyForward = -bodyForward;

        float shoulderWidth = Mathf.Max(Vector3.Distance(leftShoulder, rightShoulder), 0.25f);
        float headWidth = shoulderWidth * 0.36f;
        float headForward = shoulderWidth * 0.22f;
        float headUp = shoulderWidth * 0.16f;

        Vector3 neck = BoneOr(animator, HumanBodyBones.Neck, shoulderCenter + bodyUp * shoulderWidth * 0.18f);
        Vector3 head = BoneOr(animator, HumanBodyBones.Head, neck + bodyUp * shoulderWidth * 0.25f);
        Vector3 eyeCenter = head + bodyUp * headUp + bodyForward * headForward * 0.35f;
        Vector3 nose = eyeCenter + bodyForward * headForward * 0.7f - bodyUp * headUp * 0.25f;

        Vector3 leftEye = BoneOr(animator, HumanBodyBones.LeftEye, eyeCenter - bodyRight * headWidth * 0.25f);
        Vector3 rightEye = BoneOr(animator, HumanBodyBones.RightEye, eyeCenter + bodyRight * headWidth * 0.25f);
        Vector3 leftEar = head - bodyRight * headWidth * 0.55f;
        Vector3 rightEar = head + bodyRight * headWidth * 0.55f;
        Vector3 mouthCenter = nose - bodyUp * headUp * 0.55f - bodyForward * headForward * 0.15f;

        float handSize = shoulderWidth * 0.12f;
        float footLength = shoulderWidth * 0.35f;
        Vector3 leftToe = BoneOr(animator, HumanBodyBones.LeftToes, leftAnkle + bodyForward * footLength);
        Vector3 rightToe = BoneOr(animator, HumanBodyBones.RightToes, rightAnkle + bodyForward * footLength);

        lm[0] = nose;
        lm[1] = Vector3.Lerp(nose, leftEye, 0.65f);
        lm[2] = leftEye;
        lm[3] = leftEye - bodyRight * headWidth * 0.12f;
        lm[4] = Vector3.Lerp(nose, rightEye, 0.65f);
        lm[5] = rightEye;
        lm[6] = rightEye + bodyRight * headWidth * 0.12f;
        lm[7] = leftEar;
        lm[8] = rightEar;
        lm[9] = mouthCenter - bodyRight * headWidth * 0.18f;
        lm[10] = mouthCenter + bodyRight * headWidth * 0.18f;

        lm[11] = leftShoulder;
        lm[12] = rightShoulder;
        lm[13] = leftElbow;
        lm[14] = rightElbow;
        lm[15] = leftWrist;
        lm[16] = rightWrist;

        lm[17] = leftWrist - bodyRight * handSize;
        lm[18] = rightWrist + bodyRight * handSize;
        lm[19] = leftWrist + bodyForward * handSize * 0.8f;
        lm[20] = rightWrist + bodyForward * handSize * 0.8f;
        lm[21] = leftWrist + bodyRight * handSize * 0.55f;
        lm[22] = rightWrist - bodyRight * handSize * 0.55f;

        lm[23] = leftHip;
        lm[24] = rightHip;
        lm[25] = leftKnee;
        lm[26] = rightKnee;
        lm[27] = leftAnkle;
        lm[28] = rightAnkle;
        lm[29] = leftAnkle - bodyForward * footLength * 0.45f;
        lm[30] = rightAnkle - bodyForward * footLength * 0.45f;
        lm[31] = leftToe;
        lm[32] = rightToe;

        return lm;
    }

    private Vector3 EncodeLandmark(Vector3 landmarkWorld, Vector3 hipCenterWorld)
    {
        Vector3 local = (landmarkWorld - hipCenterWorld) * worldScale;

        if (useSceneCameraProjection && projectionCamera != null)
        {
            Transform cameraTransform = projectionCamera.transform;
            Vector3 anchor = cameraTransform.position + cameraTransform.forward * cameraDepth;
            Vector3 projectedWorld =
                anchor +
                cameraTransform.right * local.x +
                cameraTransform.up * local.y +
                cameraTransform.forward * local.z;

            return projectionCamera.WorldToViewportPoint(projectedWorld);
        }

        return new Vector3(
            0.5f + local.x * fallbackViewportScale,
            0.5f + local.y * fallbackViewportScale,
            fallbackDepth + local.z * fallbackDepthScale);
    }

    private static Vector3 BoneOr(Animator animator, HumanBodyBones bone, Vector3 fallback)
    {
        Transform transform = animator.GetBoneTransform(bone);
        return transform != null ? transform.position : fallback;
    }

    private static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
    {
        if (value.sqrMagnitude > 0.0001f)
            return value.normalized;

        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.up;
    }

    private void ApplySelection()
    {
        foreach (UnityEngine.Object selectedObject in Selection.objects)
            AssignFromObject(selectedObject);

        if (projectionCamera == null)
            projectionCamera = Camera.main;

        if (string.IsNullOrWhiteSpace(outputName) && animationClip != null)
            outputName = $"DanceClip_{animationClip.name}";

        Repaint();
    }

    private void AssignFromObject(UnityEngine.Object selectedObject)
    {
        if (selectedObject == null)
            return;

        AnimationClip selectedClip = selectedObject as AnimationClip;
        if (selectedClip != null && !IsPreviewClip(selectedClip))
        {
            animationClip = selectedClip;
            sourceAvatar = FindModelAssetForObject(selectedClip) ?? sourceAvatar;
            return;
        }

        GameObject selectedGameObject = selectedObject as GameObject;
        if (selectedGameObject != null)
        {
            sourceAvatar = selectedGameObject;
            animationClip = FindFirstUsableClip(selectedGameObject) ?? animationClip;
            return;
        }

        string path = AssetDatabase.GetAssetPath(selectedObject);
        if (string.IsNullOrEmpty(path))
            return;

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model != null)
            sourceAvatar = model;

        AnimationClip clip = FindFirstUsableClipAtPath(path);
        if (clip != null)
            animationClip = clip;
    }

    private static bool IsBakeableSelectionObject(UnityEngine.Object selectedObject)
    {
        if (selectedObject == null)
            return false;

        if (selectedObject is AnimationClip)
            return true;

        if (selectedObject is GameObject)
            return true;

        string path = AssetDatabase.GetAssetPath(selectedObject);
        return !string.IsNullOrEmpty(path) &&
               (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null || FindFirstUsableClipAtPath(path) != null);
    }

    private static AnimationClip FindFirstUsableClip(GameObject gameObject)
    {
        if (gameObject == null)
            return null;

        string path = AssetDatabase.GetAssetPath(gameObject);
        AnimationClip clip = FindFirstUsableClipAtPath(path);
        if (clip != null)
            return clip;

        Animator animator = gameObject.GetComponentInChildren<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
            return animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c != null && !IsPreviewClip(c));

        return null;
    }

    private static AnimationClip FindFirstUsableClipAtPath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;

        return AssetDatabase
            .LoadAllAssetRepresentationsAtPath(assetPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c != null && !IsPreviewClip(c));
    }

    private static GameObject FindModelAssetForObject(UnityEngine.Object assetObject)
    {
        string path = AssetDatabase.GetAssetPath(assetObject);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static bool IsPreviewClip(AnimationClip clip)
    {
        return clip == null ||
               clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase) ||
               clip.name.StartsWith("preview", StringComparison.OrdinalIgnoreCase);
    }

    private string GetEffectiveOutputName()
    {
        if (!string.IsNullOrWhiteSpace(outputName))
            return outputName;

        return animationClip != null ? $"DanceClip_{animationClip.name}" : "DanceClip_Mixamo";
    }

    private void BrowseOutputFolder()
    {
        string startFolder = AssetPathToAbsolute(outputFolder);
        string selectedFolder = EditorUtility.OpenFolderPanel("Dance Clip Output Folder", startFolder, string.Empty);

        if (string.IsNullOrEmpty(selectedFolder))
            return;

        string projectRelativePath = FileUtil.GetProjectRelativePath(selectedFolder);
        if (string.IsNullOrEmpty(projectRelativePath) || !projectRelativePath.StartsWith("Assets", StringComparison.Ordinal))
        {
            EditorUtility.DisplayDialog("Mixamo Dance Baker", "Output folder must be inside this Unity project's Assets folder.", "OK");
            return;
        }

        outputFolder = projectRelativePath.Replace('\\', '/');
    }

    private static string AssetPathToAbsolute(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets", StringComparison.Ordinal))
            return Application.dataPath;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static void EnsureAssetFolder(string folder)
    {
        folder = folder.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets", StringComparison.Ordinal))
            throw new InvalidOperationException("Output folder must be inside Assets.");

        if (AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "DanceClip_Mixamo";

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return fileName.Trim();
    }
}
#endif
