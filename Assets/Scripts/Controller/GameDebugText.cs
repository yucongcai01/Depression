using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GameDebugText : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text debugText;
    [SerializeField] private GameSceneController gameSceneController;
    [SerializeField] private AdaptiveDifficultyController adaptiveDifficultyController;
    [SerializeField] private AccuracyChecker accuracyChecker;
    [SerializeField] private DancePlayback dancePlayback;
    [SerializeField] private FaceEmotionProvider faceEmotionProvider;
    [SerializeField] private NewBodyTracker playerTracker;

    [Header("Display")]
    [SerializeField] private bool autoResolveReferences = true;
    [SerializeField] private bool createTextIfMissing = true;
    [SerializeField, Min(0.02f)] private float refreshIntervalSeconds = 0.1f;
    [SerializeField] private Vector2 anchoredPosition = new Vector2(16f, -16f);
    [SerializeField] private Vector2 size = new Vector2(760f, 920f);
    [SerializeField] private float fontSize = 16f;
    [SerializeField] private bool showCalibration = true;
    [SerializeField] private bool showSession = true;
    [SerializeField] private bool showAdaptiveDifficulty = true;
    [SerializeField] private bool showPhysicalSignals = true;
    [SerializeField] private bool showEmotionSignals = true;
    [SerializeField] private bool showPlayback = true;

    private readonly StringBuilder builder = new StringBuilder(2048);
    private float nextRefreshTime;

    private void Awake()
    {
        if (debugText == null)
            debugText = GetComponent<TMP_Text>();

        if (debugText == null && createTextIfMissing)
            debugText = CreateDebugText();

        if (autoResolveReferences)
            ResolveReferences();
    }

    private void OnEnable()
    {
        nextRefreshTime = 0f;
        RefreshDebugText();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        if (autoResolveReferences)
            ResolveReferences();

        RefreshDebugText();
        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, refreshIntervalSeconds);
    }

    public void RefreshDebugText()
    {
        if (debugText == null)
            return;

        builder.Clear();
        builder.AppendLine("GAME DEBUG");
        builder.AppendLine($"Time: {Time.time:F1}s | FPS: {GetApproximateFps():F0}");

        if (showSession)
            AppendSessionSection();

        if (showAdaptiveDifficulty)
            AppendAdaptiveDifficultySection();

        if (showPhysicalSignals)
            AppendPhysicalSignalsSection();

        if (showEmotionSignals)
            AppendEmotionSignalsSection();

        if (showPlayback)
            AppendPlaybackSection();

        if (showCalibration)
            AppendCalibrationSection();

        debugText.text = builder.ToString();
    }

    private void ResolveReferences()
    {
        if (gameSceneController == null)
            gameSceneController = FindAnyObjectByType<GameSceneController>();

        if (adaptiveDifficultyController == null)
            adaptiveDifficultyController = FindAnyObjectByType<AdaptiveDifficultyController>();

        if (accuracyChecker == null)
            accuracyChecker = FindAnyObjectByType<AccuracyChecker>();

        if (dancePlayback == null)
            dancePlayback = FindAnyObjectByType<DancePlayback>();

        if (faceEmotionProvider == null)
            faceEmotionProvider = FindAnyObjectByType<FaceEmotionProvider>();

        if (playerTracker == null)
            playerTracker = FindAnyObjectByType<NewBodyTracker>();

        if (adaptiveDifficultyController != null)
        {
            if (accuracyChecker == null)
                accuracyChecker = adaptiveDifficultyController.accuracyChecker;

            if (dancePlayback == null)
                dancePlayback = adaptiveDifficultyController.dancePlayback;

            if (faceEmotionProvider == null)
                faceEmotionProvider = adaptiveDifficultyController.faceEmotionProvider;
        }
    }

    private TMP_Text CreateDebugText()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("Debug Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
        }

        GameObject textObject = new GameObject("Game Debug Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(canvas.transform, false);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;

        return text;
    }

    private void AppendSessionSection()
    {
        builder.AppendLine();
        builder.AppendLine("[Session]");

        if (gameSceneController == null)
        {
            builder.AppendLine("GameSceneController: missing");
            return;
        }

        builder.AppendLine($"Mode: {gameSceneController.CurrentModeLabel}");
        builder.AppendLine($"Running: {FormatBool(gameSceneController.IsRunning)} | Countdown: {FormatBool(gameSceneController.IsCountingDown)}");
        builder.AppendLine($"Action: {gameSceneController.CurrentActionNumber}/{gameSceneController.TotalActions}");
        builder.AppendLine($"Clip: {FormatText(gameSceneController.CurrentClipName)}");
        builder.AppendLine($"Action Time Left: {gameSceneController.CurrentActionRemainingSeconds:F1}s / {gameSceneController.ActionDurationSeconds:F1}s");
        builder.AppendLine($"Countdown Left: {gameSceneController.CountdownRemainingSeconds:F1}s");
    }

    private void AppendAdaptiveDifficultySection()
    {
        builder.AppendLine();
        builder.AppendLine("[Adaptive Difficulty]");

        if (adaptiveDifficultyController == null)
        {
            builder.AppendLine("AdaptiveDifficultyController: missing");
            return;
        }

        builder.AppendLine($"Mode: {adaptiveDifficultyController.adaptiveMode}");
        builder.AppendLine($"Speed Base/Target/Current: {adaptiveDifficultyController.baseSpeedMultiplier:F2} / {adaptiveDifficultyController.targetSpeedMultiplier:F2} / {adaptiveDifficultyController.adjustedSpeedMultiplier:F2}");
        builder.AppendLine($"Speed Range: {adaptiveDifficultyController.minSpeedMultiplier:F2}-{adaptiveDifficultyController.maxSpeedMultiplier:F2}");
        builder.AppendLine($"Physical Output: {adaptiveDifficultyController.physicalSpeedOutput:F2}");
        builder.AppendLine($"Emotion Output: {adaptiveDifficultyController.emotionSpeedOutput:F2}");
        builder.AppendLine($"Combined Output: {adaptiveDifficultyController.fuzzyOutput:F2}");
        builder.AppendLine($"Emotion Weight: {adaptiveDifficultyController.emotionWeight:F2}");
        builder.AppendLine($"Update Interval: {adaptiveDifficultyController.difficultyUpdateInterval:F1}s");
        builder.AppendLine($"Last Update: {FormatTimeSince(adaptiveDifficultyController.lastDifficultyUpdatedAt)}");
        builder.AppendLine($"Calibration Applied: {FormatBool(adaptiveDifficultyController.calibrationBaselineApplied)}");
    }

    private void AppendPhysicalSignalsSection()
    {
        builder.AppendLine();
        builder.AppendLine("[Physical Signals]");

        if (accuracyChecker == null)
        {
            builder.AppendLine("AccuracyChecker: missing");
        }
        else
        {
            builder.AppendLine($"Frame Accuracy: {accuracyChecker.currentAccuracy:P0}");
        }

        if (adaptiveDifficultyController != null)
        {
            builder.AppendLine($"Window Accuracy: {adaptiveDifficultyController.accuracyInput:P0}");
            builder.AppendLine($"Accuracy Change: {adaptiveDifficultyController.accuracyChangeInput:+0.000;-0.000;0.000}");
            builder.AppendLine($"Physical Input Available: {FormatBool(adaptiveDifficultyController.physicalInputAvailable)}");
        }

        if (playerTracker == null)
        {
            builder.AppendLine("Player Tracker: missing");
            return;
        }

        int landmarkCount = playerTracker.currentLandmarksExternal != null
            ? playerTracker.currentLandmarksExternal.Count
            : 0;

        builder.AppendLine($"Pose Landmarks: {landmarkCount}");
        builder.AppendLine($"Mirror/FlipY: {FormatBool(playerTracker.mirror)} / {FormatBool(playerTracker.flipY)}");
        builder.AppendLine($"Depth Multiplier/Offset: {playerTracker.depthMultiplier:F2} / {playerTracker.depthOffset:F2}");
    }

    private void AppendEmotionSignalsSection()
    {
        builder.AppendLine();
        builder.AppendLine("[Emotion Signals]");

        if (faceEmotionProvider == null)
        {
            builder.AppendLine("FaceEmotionProvider: missing");
            return;
        }

        builder.AppendLine($"Face Detected: {FormatBool(faceEmotionProvider.faceDetected)}");
        builder.AppendLine($"Valence Proxy/Smoothed: {faceEmotionProvider.valenceProxy:F2} / {faceEmotionProvider.smoothedValence:F2}");
        builder.AppendLine($"Arousal Proxy/Smoothed: {faceEmotionProvider.arousalProxy:F2} / {faceEmotionProvider.smoothedArousal:F2}");

        if (adaptiveDifficultyController != null)
            builder.AppendLine($"Emotion Input Available: {FormatBool(adaptiveDifficultyController.emotionInputAvailable)}");

        builder.AppendLine($"Smile L/R: {faceEmotionProvider.GetScore("mouthSmileLeft"):F2} / {faceEmotionProvider.GetScore("mouthSmileRight"):F2}");
        builder.AppendLine($"Frown L/R: {faceEmotionProvider.GetScore("mouthFrownLeft"):F2} / {faceEmotionProvider.GetScore("mouthFrownRight"):F2}");
        builder.AppendLine($"Brow Down L/R: {faceEmotionProvider.GetScore("browDownLeft"):F2} / {faceEmotionProvider.GetScore("browDownRight"):F2}");
        builder.AppendLine($"Eye Wide L/R: {faceEmotionProvider.GetScore("eyeWideLeft"):F2} / {faceEmotionProvider.GetScore("eyeWideRight"):F2}");
        builder.AppendLine($"Jaw Open: {faceEmotionProvider.GetScore("jawOpen"):F2}");
        builder.AppendLine($"Brow Inner Up: {faceEmotionProvider.GetScore("browInnerUp"):F2}");
    }

    private void AppendPlaybackSection()
    {
        builder.AppendLine();
        builder.AppendLine("[Playback]");

        if (dancePlayback == null)
        {
            builder.AppendLine("DancePlayback: missing");
            return;
        }

        string clipName = dancePlayback.clip != null ? dancePlayback.clip.name : "None";
        builder.AppendLine($"Playing: {FormatBool(dancePlayback.IsPlaying)}");
        builder.AppendLine($"Clip: {clipName}");
        builder.AppendLine($"Playback Time: {dancePlayback.PlaybackTime:F1}s / {dancePlayback.Duration:F1}s");
        builder.AppendLine($"Speed Multiplier: {dancePlayback.speedMultiplier:F2}");
        builder.AppendLine($"Loop Clip: {FormatBool(dancePlayback.loopCurrentClip)}");
    }

    private void AppendCalibrationSection()
    {
        builder.AppendLine();
        builder.AppendLine("[Calibration]");

        CalibrationReadinessRecord record = CalibrationReadinessStore.Current;
        if (record == null)
        {
            builder.AppendLine("Calibration Store: none");
            return;
        }

        builder.AppendLine($"Initial Baseline: {record.initialDifficultyBaseline:P0}");
        builder.AppendLine($"Affective Readiness: {record.affective.readinessScore:P0}");
        builder.AppendLine($"Skills Readiness: {record.skills.readinessScore:P0}");
        builder.AppendLine($"Motor-Cognitive Readiness: {record.motorCognitive.readinessScore:P0}");
        builder.AppendLine($"Movement Accuracy: {record.motorCognitive.movementAccuracy:P0}");
        builder.AppendLine($"Reaction Time: {record.motorCognitive.reactionTimeMs:F0} ms");
    }

    private float GetApproximateFps()
    {
        return Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
    }

    private string FormatBool(bool value)
    {
        return value ? "Yes" : "No";
    }

    private string FormatText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "None" : value;
    }

    private string FormatTimeSince(float timestamp)
    {
        return timestamp >= 0f ? $"{Mathf.Max(0f, Time.time - timestamp):F1}s ago" : "None";
    }
}
