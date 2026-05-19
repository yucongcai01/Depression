using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CalibrationSceneController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject AffectiveReadinessPanel;
    [SerializeField] private GameObject TrackingCheckPanel;
    [SerializeField] private GameObject MotorCognitivePanel;

    [Header("Scene Names")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Panel Controllers")]
    [SerializeField] private AffectiveReainessPanel affectiveReadinessPanelController;
    [SerializeField] private CalibrationReadinessRecorder readinessRecorder;

    [Header("Motor-Cognitive Scene Objects")]
    [SerializeField] private GameObject playerObject;
    [SerializeField] private GameObject coachObject;
    [SerializeField] private string playerObjectName = "Player";
    [SerializeField] private string coachObjectName = "Coach";

    [Header("Motor-Cognitive Readiness")]
    [SerializeField] private DancePlayback motorCognitivePlayback;
    [SerializeField] private AccuracyChecker motorCognitiveAccuracyChecker;
    [SerializeField] private PoseLandmarkerRunner motorCognitivePoseRunner;
    [SerializeField] private Button motorCognitiveContinueButton;
    [SerializeField] private TMP_Text motorCognitiveStatusText;
    [SerializeField] private TMP_Text motorCognitiveButtonText;
    [SerializeField] private GameObject motorCognitiveVisualRoot;
    [SerializeField] private GameObject motorCognitiveAvatarPrefab;
    [SerializeField] private DanceClipData[] motorCognitiveClips;
    [SerializeField] private string[] defaultMotorCognitiveClipResourcePaths =
    {
        "DanceClips/Starter1",
        "DanceClips/Jazz1",
        "DanceClips/Jazz2"
    };
    [SerializeField] private bool autoCreateMotorCognitivePlayback = true;
    [SerializeField] private bool autoStartMotorCognitiveSequence = true;
    [SerializeField] private float motorCognitiveClipPlaybackSeconds = 10f;
    [SerializeField] private float interClipDelaySeconds = 0.5f;
    [SerializeField] private float maxDistanceForZeroMovementScore = 0.5f;
    [SerializeField, Range(0f, 1f)] private float engagementAccuracyThreshold = 0.2f;
    [SerializeField] private bool mirrorPoseLandmarks;
    [SerializeField] private bool flipPoseLandmarksY = true;
    [SerializeField] private float poseDepthMultiplier = 1f;
    [SerializeField] private float poseDepthOffset = 5f;
    [SerializeField] private Vector3 motorCognitiveAvatarPosition = new Vector3(0f, 0f, 0f);
    [SerializeField] private Vector3 motorCognitiveAvatarRotation = new Vector3(0f, 180f, 0f);
    [SerializeField] private Vector3 motorCognitiveAvatarScale = Vector3.one;
#if UNITY_EDITOR
    [SerializeField] private string editorFallbackMotorCognitiveAvatarPath =
        "Assets/Kevin Iglesias/Human Character Dummy/Prefabs/HumanDummy_M White.prefab";
#endif

    private bool motorCognitiveInitialized;
    private bool motorCognitiveSequenceStarted;
    private bool motorCognitiveAssessmentCompleted;
    private bool waitingForNextMotorCognitiveClip;
    private int motorCognitiveClipIndex = -1;
    private int completedMotorCognitiveClipCount;
    private float currentMotorCognitiveClipStartedAt;
    private float nextMotorCognitiveClipStartAt;
    private float currentMotorCognitiveAccuracySum;
    private float currentMotorCognitiveAccuracyTime;
    private float totalMotorCognitiveAccuracySum;
    private float totalMotorCognitiveAccuracyTime;
    private float firstMotorCognitiveReactionTimeMs = -1f;
    private float finalMotorCognitiveMovementAccuracy = 0.5f;
    private GameObject runtimeMotorCognitiveAvatar;
    private bool calibrationSessionFinalized;

    public CalibrationAffectiveReadinessData AffectiveReadinessData { get; private set; }

    void Start()
    {
        if (affectiveReadinessPanelController == null && AffectiveReadinessPanel != null)
            affectiveReadinessPanelController = AffectiveReadinessPanel.GetComponent<AffectiveReainessPanel>();

        if (readinessRecorder == null)
            readinessRecorder = GetComponent<CalibrationReadinessRecorder>();

        if (readinessRecorder == null)
            readinessRecorder = gameObject.AddComponent<CalibrationReadinessRecorder>();

        if (affectiveReadinessPanelController != null)
            affectiveReadinessPanelController.Initialize(this);

        ResolveMotorCognitiveSceneObjects();
        PrepareMotorCognitivePanel();
        ShowAffectiveReadinessPanel();
    }

    private void Update()
    {
        UpdateMotorCognitiveSequence();
    }

    public void ShowAffectiveReadinessPanel()
    {
        SetPanelActive(AffectiveReadinessPanel, true);
        SetPanelActive(TrackingCheckPanel, false);
        SetPanelActive(MotorCognitivePanel, false);
        SetMotorCognitiveVisualVisible(false);
        SetMotorCognitiveActorsActive(false);
    }
    
    public void ShowTrackingCheckPanel()
    {
        SetPanelActive(AffectiveReadinessPanel, false);
        SetPanelActive(TrackingCheckPanel, true);
        SetPanelActive(MotorCognitivePanel, false);
        SetMotorCognitiveVisualVisible(false);
        SetMotorCognitiveActorsActive(false);
    }
    
    public void ShowMotorCognitivePanel()
    {
        SetPanelActive(AffectiveReadinessPanel, false);
        SetPanelActive(TrackingCheckPanel, false);
        SetPanelActive(MotorCognitivePanel, true);

        PrepareMotorCognitivePanel();
        SetMotorCognitiveActorsActive(true);
        SetMotorCognitiveVisualVisible(true);

        if (autoStartMotorCognitiveSequence && !motorCognitiveSequenceStarted && !motorCognitiveAssessmentCompleted)
            BeginMotorCognitiveSequence();
    }

    public void ContinueFromAffectiveReadiness()
    {
        if (affectiveReadinessPanelController != null)
        {
            if (!affectiveReadinessPanelController.CanContinue)
                return;

            AffectiveReadinessData = affectiveReadinessPanelController.GetData();
            if (readinessRecorder != null)
                readinessRecorder.RecordAffectiveReadiness(AffectiveReadinessData);
        }

        ShowTrackingCheckPanel();
    }

    public void ContinueFromTrackingCheck()
    {
        if (readinessRecorder != null)
            readinessRecorder.RecordPhysicalReadiness(trackingConfirmed: true);

        ShowMotorCognitivePanel();
    }

    public void ContinueFromMotorCognitive()
    {
        PrepareMotorCognitivePanel();

        if (!motorCognitiveAssessmentCompleted)
        {
            if (!motorCognitiveSequenceStarted)
                BeginMotorCognitiveSequence();
            else
                RefreshMotorCognitiveUi();

            return;
        }

        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError("CalibrationSceneController: Game scene name is empty.");
            return;
        }

        if (readinessRecorder != null)
        {
            FinalizeCalibrationSessionIfNeeded();
        }

        SceneManager.LoadScene(gameSceneName);
    }

    private void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
            panel.SetActive(active);
    }

    private void PrepareMotorCognitivePanel()
    {
        if (motorCognitiveInitialized)
            return;

        if (motorCognitivePlayback == null)
            motorCognitivePlayback = FindMotorCognitivePlayback();

        EnsureMotorCognitivePlayback();

        if (motorCognitiveAccuracyChecker == null)
            motorCognitiveAccuracyChecker = FindAnyObjectByType<AccuracyChecker>();

        if (motorCognitivePoseRunner == null)
            motorCognitivePoseRunner = FindAnyObjectByType<PoseLandmarkerRunner>();

        if (MotorCognitivePanel != null)
        {
            if (motorCognitiveContinueButton == null)
                motorCognitiveContinueButton = MotorCognitivePanel.GetComponentInChildren<Button>(true);

            if (motorCognitiveButtonText == null && motorCognitiveContinueButton != null)
                motorCognitiveButtonText = motorCognitiveContinueButton.GetComponentInChildren<TMP_Text>(true);

            if (motorCognitiveStatusText == null)
            {
                TMP_Text[] texts = MotorCognitivePanel.GetComponentsInChildren<TMP_Text>(true);
                foreach (TMP_Text text in texts)
                {
                    if (motorCognitiveContinueButton == null || !text.transform.IsChildOf(motorCognitiveContinueButton.transform))
                    {
                        motorCognitiveStatusText = text;
                        break;
                    }
                }
            }

            if (motorCognitiveStatusText == null)
                motorCognitiveStatusText = CreateMotorCognitiveStatusText();
        }

        LoadDefaultMotorCognitiveClipsIfNeeded();
        WireMotorCognitiveScoringReferences();
        motorCognitiveInitialized = true;
        SetMotorCognitiveVisualVisible(false);
        RefreshMotorCognitiveUi();
    }

    private void EnsureMotorCognitivePlayback()
    {
        if (motorCognitivePlayback != null || !autoCreateMotorCognitivePlayback)
            return;

        GameObject prefab = motorCognitiveAvatarPrefab;
#if UNITY_EDITOR
        if (prefab == null && !string.IsNullOrWhiteSpace(editorFallbackMotorCognitiveAvatarPath))
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(editorFallbackMotorCognitiveAvatarPath);
#endif

        if (prefab == null)
            return;

        runtimeMotorCognitiveAvatar = Instantiate(
            prefab,
            motorCognitiveAvatarPosition,
            Quaternion.Euler(motorCognitiveAvatarRotation));
        runtimeMotorCognitiveAvatar.name = "Motor-Cognitive Reference Avatar";
        runtimeMotorCognitiveAvatar.transform.localScale = motorCognitiveAvatarScale;

        Animator animator = runtimeMotorCognitiveAvatar.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;
        }

        motorCognitivePlayback = runtimeMotorCognitiveAvatar.GetComponentInChildren<DancePlayback>();
        if (motorCognitivePlayback == null)
            motorCognitivePlayback = runtimeMotorCognitiveAvatar.AddComponent<DancePlayback>();

        motorCognitivePlayback.animator = animator;
        motorCognitivePlayback.loopCurrentClip = false;

        if (motorCognitiveVisualRoot == null)
            motorCognitiveVisualRoot = runtimeMotorCognitiveAvatar;
    }

    private void LoadDefaultMotorCognitiveClipsIfNeeded()
    {
        if (HasMotorCognitiveClips())
            return;

        if (defaultMotorCognitiveClipResourcePaths == null || defaultMotorCognitiveClipResourcePaths.Length == 0)
            return;

        DanceClipData[] loadedClips = new DanceClipData[defaultMotorCognitiveClipResourcePaths.Length];
        int loadedCount = 0;

        for (int i = 0; i < defaultMotorCognitiveClipResourcePaths.Length; i++)
        {
            string resourcePath = defaultMotorCognitiveClipResourcePaths[i];
            if (string.IsNullOrWhiteSpace(resourcePath))
                continue;

            resourcePath = NormalizeResourcesPath(resourcePath);
            DanceClipData clip = Resources.Load<DanceClipData>(resourcePath);
            if (clip == null)
            {
                Debug.LogWarning($"CalibrationSceneController: Could not load motor-cognitive clip at Resources/{resourcePath}.");
                continue;
            }

            loadedClips[loadedCount] = clip;
            loadedCount++;
        }

        if (loadedCount == loadedClips.Length)
        {
            motorCognitiveClips = loadedClips;
            return;
        }

        motorCognitiveClips = new DanceClipData[loadedCount];
        for (int i = 0; i < loadedCount; i++)
            motorCognitiveClips[i] = loadedClips[i];
    }

    private string NormalizeResourcesPath(string resourcePath)
    {
        resourcePath = resourcePath.Replace("\\", "/").Trim();

        const string resourcesPrefix = "Resources/";
        int resourcesIndex = resourcePath.IndexOf(resourcesPrefix, System.StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex >= 0)
            resourcePath = resourcePath.Substring(resourcesIndex + resourcesPrefix.Length);

        if (resourcePath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            resourcePath = resourcePath.Substring(0, resourcePath.Length - ".asset".Length);

        return resourcePath;
    }

    private TMP_Text CreateMotorCognitiveStatusText()
    {
        if (MotorCognitivePanel == null)
            return null;

        GameObject statusObject = new GameObject("Motor-Cognitive Status", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        statusObject.transform.SetParent(MotorCognitivePanel.transform, false);

        RectTransform rectTransform = statusObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(0f, -390f);
        rectTransform.sizeDelta = new Vector2(1200f, 260f);

        TMP_Text text = statusObject.GetComponent<TMP_Text>();
        text.color = Color.white;
        text.fontSize = 28f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;

        return text;
    }

    private void WireMotorCognitiveScoringReferences()
    {
        if (motorCognitiveAccuracyChecker == null)
            return;

        if (motorCognitivePlayback != null && motorCognitiveAccuracyChecker.referencePlayback == null)
            motorCognitiveAccuracyChecker.referencePlayback = motorCognitivePlayback;

        if (motorCognitiveAccuracyChecker.playerTracker == null)
            motorCognitiveAccuracyChecker.playerTracker = FindPlayerTracker();
    }

    private void ResolveMotorCognitiveSceneObjects()
    {
        if (playerObject == null)
            playerObject = FindSceneObjectByName(playerObjectName);

        if (coachObject == null)
            coachObject = FindSceneObjectByName(coachObjectName);
    }

    private DancePlayback FindMotorCognitivePlayback()
    {
        ResolveMotorCognitiveSceneObjects();

        DancePlayback playback = GetComponentInChildrenIncludingInactive<DancePlayback>(coachObject);
        if (playback != null)
            return playback;

        playback = GetComponentInChildrenIncludingInactive<DancePlayback>(playerObject);
        return playback != null ? playback : FindAnyObjectByType<DancePlayback>();
    }

    private NewBodyTracker FindPlayerTracker()
    {
        ResolveMotorCognitiveSceneObjects();

        NewBodyTracker playerTracker = GetComponentInChildrenIncludingInactive<NewBodyTracker>(playerObject);
        return playerTracker != null ? playerTracker : FindAnyObjectByType<NewBodyTracker>();
    }

    private GameObject FindSceneObjectByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject obj in objects)
        {
            if (obj == null || obj.name != objectName || !obj.scene.IsValid())
                continue;

            return obj;
        }

        return null;
    }

    private T GetComponentInChildrenIncludingInactive<T>(GameObject root) where T : Component
    {
        return root != null ? root.GetComponentInChildren<T>(true) : null;
    }

    private void SetMotorCognitiveActorsActive(bool active)
    {
        ResolveMotorCognitiveSceneObjects();
        SetObjectActive(playerObject, active);
        SetObjectActive(coachObject, active);
    }

    private void SetObjectActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private bool HasMotorCognitiveClips()
    {
        if (motorCognitiveClips == null)
            return false;

        foreach (DanceClipData clip in motorCognitiveClips)
        {
            if (clip != null)
                return true;
        }

        return false;
    }

    private void BeginMotorCognitiveSequence()
    {
        LoadDefaultMotorCognitiveClipsIfNeeded();
        WireMotorCognitiveScoringReferences();

        motorCognitiveSequenceStarted = true;
        motorCognitiveAssessmentCompleted = false;
        waitingForNextMotorCognitiveClip = false;
        motorCognitiveClipIndex = -1;
        completedMotorCognitiveClipCount = 0;
        currentMotorCognitiveAccuracySum = 0f;
        currentMotorCognitiveAccuracyTime = 0f;
        totalMotorCognitiveAccuracySum = 0f;
        totalMotorCognitiveAccuracyTime = 0f;
        firstMotorCognitiveReactionTimeMs = -1f;
        finalMotorCognitiveMovementAccuracy = 0.5f;
        calibrationSessionFinalized = false;

        if (!HasMotorCognitiveClips())
        {
            Debug.LogWarning("CalibrationSceneController: No motor-cognitive clips are configured. Recording neutral readiness.");
            CompleteMotorCognitiveAssessment(0.5f, 1200f);
            return;
        }

        if (motorCognitivePlayback == null)
            Debug.LogWarning("CalibrationSceneController: No DancePlayback found. The readiness score will be neutral unless a playback component is assigned.");

        PlayNextMotorCognitiveClip();
    }

    private void UpdateMotorCognitiveSequence()
    {
        if (!motorCognitiveSequenceStarted || motorCognitiveAssessmentCompleted)
            return;

        if (waitingForNextMotorCognitiveClip)
        {
            if (Time.time >= nextMotorCognitiveClipStartAt)
            {
                waitingForNextMotorCognitiveClip = false;
                PlayNextMotorCognitiveClip();
            }

            return;
        }

        DanceClipData currentClip = GetCurrentMotorCognitiveClip();
        if (currentClip == null)
            return;

        SampleMotorCognitiveAccuracy();

        float duration = GetMotorCognitiveClipPlaybackDuration(currentClip);
        float elapsed = Time.time - currentMotorCognitiveClipStartedAt;

        if (elapsed >= duration)
            CompleteCurrentMotorCognitiveClip();
    }

    private void PlayNextMotorCognitiveClip()
    {
        motorCognitiveClipIndex = FindNextMotorCognitiveClipIndex(motorCognitiveClipIndex + 1);
        DanceClipData clip = GetCurrentMotorCognitiveClip();

        if (clip == null)
        {
            float averageAccuracy = totalMotorCognitiveAccuracyTime > 0f
                ? totalMotorCognitiveAccuracySum / totalMotorCognitiveAccuracyTime
                : 0.5f;

            float reactionTimeMs = firstMotorCognitiveReactionTimeMs >= 0f
                ? firstMotorCognitiveReactionTimeMs
                : 1200f;

            CompleteMotorCognitiveAssessment(averageAccuracy, reactionTimeMs);
            return;
        }

        currentMotorCognitiveAccuracySum = 0f;
        currentMotorCognitiveAccuracyTime = 0f;
        currentMotorCognitiveClipStartedAt = Time.time;

        if (motorCognitivePlayback != null)
        {
            motorCognitivePlayback.enabled = true;
            motorCognitivePlayback.clip = clip;
            motorCognitivePlayback.loopCurrentClip = true;
            motorCognitivePlayback.Seek(0f);
            motorCognitivePlayback.Play();
        }

        RefreshMotorCognitiveUi();
    }

    private int FindNextMotorCognitiveClipIndex(int startIndex)
    {
        if (motorCognitiveClips == null)
            return -1;

        for (int i = startIndex; i < motorCognitiveClips.Length; i++)
        {
            if (motorCognitiveClips[i] != null)
                return i;
        }

        return -1;
    }

    private DanceClipData GetCurrentMotorCognitiveClip()
    {
        if (motorCognitiveClips == null || motorCognitiveClipIndex < 0 || motorCognitiveClipIndex >= motorCognitiveClips.Length)
            return null;

        return motorCognitiveClips[motorCognitiveClipIndex];
    }

    private float GetMotorCognitiveClipPlaybackDuration(DanceClipData clip)
    {
        if (motorCognitiveClipPlaybackSeconds > 0f)
            return motorCognitiveClipPlaybackSeconds;

        return Mathf.Max(0.01f, clip != null ? clip.Duration : 0.01f);
    }

    private void SampleMotorCognitiveAccuracy()
    {
        if (!TryGetMotorCognitiveAccuracy(out float accuracy))
            accuracy = 0.5f;

        float deltaTime = Mathf.Max(Time.deltaTime, 0f);
        currentMotorCognitiveAccuracySum += Mathf.Clamp01(accuracy) * deltaTime;
        currentMotorCognitiveAccuracyTime += deltaTime;

        if (firstMotorCognitiveReactionTimeMs < 0f && accuracy >= engagementAccuracyThreshold)
            firstMotorCognitiveReactionTimeMs = Mathf.Max(0f, (Time.time - currentMotorCognitiveClipStartedAt) * 1000f);
    }

    private bool TryGetMotorCognitiveAccuracy(out float accuracy)
    {
        accuracy = 0f;

        if (motorCognitiveAccuracyChecker != null && motorCognitiveAccuracyChecker.enabled)
        {
            accuracy = motorCognitiveAccuracyChecker.currentAccuracy;
            return true;
        }

        return TryGetPoseRunnerMotorCognitiveAccuracy(out accuracy);
    }

    private bool TryGetPoseRunnerMotorCognitiveAccuracy(out float accuracy)
    {
        accuracy = 0f;

        if (motorCognitivePoseRunner == null || motorCognitivePlayback == null)
            return false;

        var result = motorCognitivePoseRunner.latestResult;
        if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
            return false;

        var liveLandmarks = result.poseLandmarks[0].landmarks;
        var referenceLandmarks = motorCognitivePlayback.GetCurrentLandmarks();
        int[] recordedIndices = motorCognitivePlayback.RecordedIndices;

        if (liveLandmarks == null || liveLandmarks.Count < 33 || referenceLandmarks == null || referenceLandmarks.Count < 33)
            return false;

        if (recordedIndices == null || recordedIndices.Length == 0)
        {
            recordedIndices = new int[33];
            for (int i = 0; i < recordedIndices.Length; i++)
                recordedIndices[i] = i;
        }

        float totalDistance = 0f;
        int comparedCount = 0;

        foreach (int index in recordedIndices)
        {
            if (index < 0 || index >= liveLandmarks.Count || index >= referenceLandmarks.Count)
                continue;

            var live = liveLandmarks[index];
            var reference = referenceLandmarks[index];
            Vector3 livePoint = new Vector3(
                mirrorPoseLandmarks ? 1f - live.x : live.x,
                flipPoseLandmarksY ? 1f - live.y : live.y,
                live.z * poseDepthMultiplier + poseDepthOffset);
            Vector3 referencePoint = new Vector3(reference.x, reference.y, reference.z);

            totalDistance += Vector3.Distance(livePoint, referencePoint);
            comparedCount++;
        }

        if (comparedCount == 0)
            return false;

        float averageDistance = totalDistance / comparedCount;
        accuracy = Mathf.Clamp01(1f - averageDistance / Mathf.Max(0.001f, maxDistanceForZeroMovementScore));
        return true;
    }

    private void CompleteCurrentMotorCognitiveClip()
    {
        DanceClipData currentClip = GetCurrentMotorCognitiveClip();
        if (motorCognitivePlayback != null && motorCognitivePlayback.clip == currentClip)
        {
            motorCognitivePlayback.Pause();
            motorCognitivePlayback.Seek(motorCognitivePlayback.Duration);
        }

        float clipDuration = GetMotorCognitiveClipPlaybackDuration(currentClip);
        float sampleTime = currentMotorCognitiveAccuracyTime > 0f ? currentMotorCognitiveAccuracyTime : clipDuration;
        float averageAccuracy = currentMotorCognitiveAccuracyTime > 0f
            ? currentMotorCognitiveAccuracySum / currentMotorCognitiveAccuracyTime
            : 0.5f;

        totalMotorCognitiveAccuracySum += Mathf.Clamp01(averageAccuracy) * sampleTime;
        totalMotorCognitiveAccuracyTime += sampleTime;
        completedMotorCognitiveClipCount++;

        int nextIndex = FindNextMotorCognitiveClipIndex(motorCognitiveClipIndex + 1);
        if (nextIndex >= 0)
        {
            waitingForNextMotorCognitiveClip = true;
            nextMotorCognitiveClipStartAt = Time.time + Mathf.Max(0f, interClipDelaySeconds);
            RefreshMotorCognitiveUi();
            return;
        }

        float finalAccuracy = totalMotorCognitiveAccuracyTime > 0f
            ? totalMotorCognitiveAccuracySum / totalMotorCognitiveAccuracyTime
            : 0.5f;
        float reactionTimeMs = firstMotorCognitiveReactionTimeMs >= 0f
            ? firstMotorCognitiveReactionTimeMs
            : 1200f;

        CompleteMotorCognitiveAssessment(finalAccuracy, reactionTimeMs);
    }

    private void CompleteMotorCognitiveAssessment(float movementAccuracy, float reactionTimeMs)
    {
        motorCognitiveAssessmentCompleted = true;
        waitingForNextMotorCognitiveClip = false;
        finalMotorCognitiveMovementAccuracy = Mathf.Clamp01(movementAccuracy);
        firstMotorCognitiveReactionTimeMs = Mathf.Max(0f, reactionTimeMs);

        if (readinessRecorder != null)
        {
            readinessRecorder.SetMovementAccuracy(finalMotorCognitiveMovementAccuracy);
            readinessRecorder.SetReactionTime(firstMotorCognitiveReactionTimeMs);
        }

        FinalizeCalibrationSessionIfNeeded();
        RefreshMotorCognitiveUi();
    }

    private void RefreshMotorCognitiveUi()
    {
        if (motorCognitiveContinueButton != null)
            motorCognitiveContinueButton.interactable = !motorCognitiveSequenceStarted || motorCognitiveAssessmentCompleted;

        if (motorCognitiveButtonText != null)
        {
            if (motorCognitiveAssessmentCompleted)
                motorCognitiveButtonText.text = "Start Game";
            else if (motorCognitiveSequenceStarted)
                motorCognitiveButtonText.text = "Playing...";
            else
                motorCognitiveButtonText.text = "Start";
        }

        if (motorCognitiveStatusText == null)
            return;

        if (motorCognitiveAssessmentCompleted)
        {
            FinalizeCalibrationSessionIfNeeded();
            motorCognitiveStatusText.text = BuildCalibrationResultsText();
            return;
        }

        if (!motorCognitiveSequenceStarted)
        {
            motorCognitiveStatusText.text = "Press Start to play the motor-cognitive readiness clips.";
            return;
        }

        if (waitingForNextMotorCognitiveClip)
        {
            motorCognitiveStatusText.text = "Preparing next clip...";
            return;
        }

        DanceClipData currentClip = GetCurrentMotorCognitiveClip();
        int clipNumber = completedMotorCognitiveClipCount + 1;
        int clipCount = CountMotorCognitiveClips();
        motorCognitiveStatusText.text = currentClip != null
            ? $"Playing {clipNumber}/{clipCount}: {currentClip.name}"
            : "Preparing motor-cognitive clips...";
    }

    private int CountMotorCognitiveClips()
    {
        int count = 0;

        if (motorCognitiveClips == null)
            return count;

        foreach (DanceClipData clip in motorCognitiveClips)
        {
            if (clip != null)
                count++;
        }

        return count;
    }

    private void FinalizeCalibrationSessionIfNeeded()
    {
        if (calibrationSessionFinalized || readinessRecorder == null)
            return;

        readinessRecorder.RecordMotorCognitiveReadiness();
        readinessRecorder.FinalizeSession();
        calibrationSessionFinalized = true;
    }

    private string BuildCalibrationResultsText()
    {
        if (readinessRecorder == null)
        {
            return
                "Calibration results\n" +
                $"Motor-cognitive readiness: {finalMotorCognitiveMovementAccuracy:P0}\n" +
                $"Movement accuracy: {finalMotorCognitiveMovementAccuracy:P0} | Reaction: {firstMotorCognitiveReactionTimeMs:F0} ms";
        }

        CalibrationReadinessRecord record = readinessRecorder.CurrentRecord;
        float baseline = Mathf.Clamp01(record.initialDifficultyBaseline);
        string difficultyLabel = GetInitialDifficultyLabel(baseline);

        return
            "Calibration results\n" +
            $"Affective readiness: {record.affective.readinessScore:P0} | Skills readiness: {record.skills.readinessScore:P0} | Motor-cognitive readiness: {record.motorCognitive.readinessScore:P0}\n" +
            $"Movement accuracy: {record.motorCognitive.movementAccuracy:P0} | Reaction: {record.motorCognitive.reactionTimeMs:F0} ms\n" +
            $"Initial difficulty baseline: {baseline:P0} ({difficultyLabel})";
    }

    private string GetInitialDifficultyLabel(float baseline)
    {
        if (baseline < 0.4f)
            return "Easier start";

        if (baseline > 0.6f)
            return "Harder start";

        return "Balanced start";
    }

    private void SetMotorCognitiveVisualVisible(bool visible)
    {
        if (motorCognitiveVisualRoot == null)
            return;

        Renderer[] renderers = motorCognitiveVisualRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
            renderer.enabled = visible;
    }
}
