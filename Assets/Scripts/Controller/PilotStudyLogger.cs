using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class PilotStudyLogger : MonoBehaviour
{
    [Header("Participant")]
    [SerializeField] private string participantId = "P001";
    [SerializeField] private string sessionIdPrefix = "pilot";

    [Header("References")]
    [SerializeField] private GameSceneController gameSceneController;
    [SerializeField] private AdaptiveDifficultyController adaptiveDifficultyController;
    [SerializeField] private AccuracyChecker accuracyChecker;
    [SerializeField] private DancePlayback dancePlayback;
    [SerializeField] private FaceEmotionProvider faceEmotionProvider;
    [SerializeField] private NewBodyTracker playerTracker;

    [Header("Logging")]
    [SerializeField] private bool loggingEnabled = true;
    [SerializeField] private bool autoResolveReferences = true;
    [SerializeField, Min(0.25f)] private float sampleIntervalSeconds = 1f;
    [SerializeField] private string outputFolderName = "PilotLogs";
    [SerializeField] private string summaryFileName = "pilot_summary.csv";
    [SerializeField] private string timeseriesFileName = "pilot_timeseries.csv";

    [Header("Output (Read Only)")]
    [SerializeField] private string sessionId;
    [SerializeField] private string outputDirectory;
    [SerializeField] private string summaryPath;
    [SerializeField] private string timeseriesPath;
    [SerializeField] private int trialIndex;
    [SerializeField] private bool trialActive;

    private readonly List<string> clipOrder = new List<string>();
    private readonly StringBuilder lineBuilder = new StringBuilder(512);

    private bool wasRunning;
    private float nextSampleTime;
    private float trialStartTime;
    private string trialStartedAtUtc;
    private AdaptiveDifficultyMode trialMode;

    private int summarySampleCount;
    private float accuracySum;
    private float valenceSum;
    private float arousalSum;
    private float speedSum;
    private int faceDetectedCount;
    private int trackingAvailableCount;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private void Awake()
    {
        sessionId = BuildSessionId();
        outputDirectory = Path.Combine(Application.persistentDataPath, outputFolderName);
        summaryPath = Path.Combine(outputDirectory, summaryFileName);
        timeseriesPath = Path.Combine(outputDirectory, timeseriesFileName);

        if (autoResolveReferences)
            ResolveReferences();

        EnsureLogFiles();
    }

    private void Update()
    {
        if (!loggingEnabled)
            return;

        if (autoResolveReferences)
            ResolveReferences();

        if (gameSceneController == null)
            return;

        bool running = gameSceneController.IsRunning;
        if (running && !wasRunning)
            BeginTrial();

        if (trialActive && running)
            UpdateTrialSampling();

        if (!running && wasRunning)
            EndTrial();

        wasRunning = running;
    }

    private void OnApplicationQuit()
    {
        if (trialActive)
            EndTrial();
    }

    public void SetParticipantId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        participantId = SanitizeCsvValue(value.Trim());
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
    }

    private void BeginTrial()
    {
        trialIndex++;
        trialActive = true;
        trialStartTime = Time.time;
        trialStartedAtUtc = DateTime.UtcNow.ToString("O");
        trialMode = gameSceneController.CurrentAdaptiveMode;
        nextSampleTime = Time.time;

        clipOrder.Clear();
        summarySampleCount = 0;
        accuracySum = 0f;
        valenceSum = 0f;
        arousalSum = 0f;
        speedSum = 0f;
        faceDetectedCount = 0;
        trackingAvailableCount = 0;
    }

    private void UpdateTrialSampling()
    {
        if (Time.time < nextSampleTime)
            return;

        nextSampleTime = Time.time + Mathf.Max(0.25f, sampleIntervalSeconds);

        if (gameSceneController.IsCountingDown || gameSceneController.CurrentActionNumber <= 0)
            return;

        CaptureSample(writeTimeseries: true);
    }

    private void EndTrial()
    {
        if (!trialActive)
            return;

        WriteSummaryRow();
        trialActive = false;
    }

    private void CaptureSample(bool writeTimeseries)
    {
        float accuracy = accuracyChecker != null ? Mathf.Clamp01(accuracyChecker.currentAccuracy) : 0f;
        float valence = faceEmotionProvider != null ? faceEmotionProvider.smoothedValence : 0f;
        float arousal = faceEmotionProvider != null ? faceEmotionProvider.smoothedArousal : 0f;
        float targetSpeed = adaptiveDifficultyController != null ? adaptiveDifficultyController.targetSpeedMultiplier : 0f;
        float currentSpeed = dancePlayback != null ? dancePlayback.speedMultiplier : 0f;
        bool faceDetected = faceEmotionProvider != null && faceEmotionProvider.faceDetected;
        int poseLandmarkCount = GetPoseLandmarkCount();
        bool trackingAvailable = poseLandmarkCount > 0;
        string clipName = gameSceneController.CurrentClipName;

        if (!string.IsNullOrWhiteSpace(clipName) && !clipOrder.Contains(clipName))
            clipOrder.Add(clipName);

        summarySampleCount++;
        accuracySum += accuracy;
        valenceSum += valence;
        arousalSum += arousal;
        speedSum += currentSpeed;

        if (faceDetected)
            faceDetectedCount++;

        if (trackingAvailable)
            trackingAvailableCount++;

        if (writeTimeseries)
            WriteTimeseriesRow(accuracy, valence, arousal, targetSpeed, currentSpeed, faceDetected, poseLandmarkCount);
    }

    private void WriteTimeseriesRow(
        float accuracy,
        float valence,
        float arousal,
        float targetSpeed,
        float currentSpeed,
        bool faceDetected,
        int poseLandmarkCount)
    {
        lineBuilder.Clear();
        AppendCsv(lineBuilder, participantId);
        AppendCsv(lineBuilder, sessionId);
        AppendCsv(lineBuilder, trialIndex);
        AppendCsv(lineBuilder, trialMode.ToString());
        AppendCsv(lineBuilder, Time.time - trialStartTime);
        AppendCsv(lineBuilder, gameSceneController.CurrentActionNumber);
        AppendCsv(lineBuilder, gameSceneController.CurrentClipName);
        AppendCsv(lineBuilder, accuracy);
        AppendCsv(lineBuilder, valence);
        AppendCsv(lineBuilder, arousal);
        AppendCsv(lineBuilder, targetSpeed);
        AppendCsv(lineBuilder, currentSpeed);
        AppendCsv(lineBuilder, faceDetected);
        AppendCsv(lineBuilder, poseLandmarkCount, finalColumn: true);

        File.AppendAllText(timeseriesPath, lineBuilder + Environment.NewLine, Encoding.UTF8);
    }

    private void WriteSummaryRow()
    {
        float baseline = CalibrationReadinessStore.Current != null
            ? CalibrationReadinessStore.Current.initialDifficultyBaseline
            : 0f;

        float meanAccuracy = summarySampleCount > 0 ? accuracySum / summarySampleCount : 0f;
        float meanValence = summarySampleCount > 0 ? valenceSum / summarySampleCount : 0f;
        float meanArousal = summarySampleCount > 0 ? arousalSum / summarySampleCount : 0f;
        float meanSpeed = summarySampleCount > 0 ? speedSum / summarySampleCount : 0f;
        float finalSpeed = dancePlayback != null ? dancePlayback.speedMultiplier : 0f;
        float faceDetectedRatio = summarySampleCount > 0 ? (float)faceDetectedCount / summarySampleCount : 0f;
        float trackingAvailableRatio = summarySampleCount > 0 ? (float)trackingAvailableCount / summarySampleCount : 0f;

        lineBuilder.Clear();
        AppendCsv(lineBuilder, participantId);
        AppendCsv(lineBuilder, sessionId);
        AppendCsv(lineBuilder, trialIndex);
        AppendCsv(lineBuilder, trialMode.ToString());
        AppendCsv(lineBuilder, string.Join("|", clipOrder));
        AppendCsv(lineBuilder, baseline);
        AppendCsv(lineBuilder, meanAccuracy);
        AppendCsv(lineBuilder, meanValence);
        AppendCsv(lineBuilder, meanArousal);
        AppendCsv(lineBuilder, meanSpeed);
        AppendCsv(lineBuilder, finalSpeed);
        AppendCsv(lineBuilder, faceDetectedRatio);
        AppendCsv(lineBuilder, trackingAvailableRatio);
        AppendCsv(lineBuilder, summarySampleCount);
        AppendCsv(lineBuilder, trialStartedAtUtc);
        AppendCsv(lineBuilder, DateTime.UtcNow.ToString("O"), finalColumn: true);

        File.AppendAllText(summaryPath, lineBuilder + Environment.NewLine, Encoding.UTF8);
    }

    private void EnsureLogFiles()
    {
        Directory.CreateDirectory(outputDirectory);

        EnsureCsvHeader(
            summaryPath,
            "participantId,sessionId,trialIndex,mode,clipOrder,baseline,meanAccuracy,meanValence,meanArousal,meanSpeed,finalSpeed,faceDetectedRatio,trackingAvailableRatio,sampleCount,startedAtUtc,endedAtUtc");

        EnsureCsvHeader(
            timeseriesPath,
            "participantId,sessionId,trialIndex,mode,time,actionNumber,clipName,accuracy,valence,arousal,targetSpeed,currentSpeed,faceDetected,poseLandmarkCount");
    }

    private void EnsureCsvHeader(string path, string header)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return;

        File.WriteAllText(path, header + Environment.NewLine, Encoding.UTF8);
    }

    private int GetPoseLandmarkCount()
    {
        return playerTracker != null && playerTracker.currentLandmarksExternal != null
            ? playerTracker.currentLandmarksExternal.Count
            : 0;
    }

    private string BuildSessionId()
    {
        string safeParticipantId = SanitizeFileName(participantId);
        string safePrefix = SanitizeFileName(sessionIdPrefix);
        return $"{safePrefix}_{safeParticipantId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
    }

    private string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidChar, '_');

        return value.Replace(' ', '_');
    }

    private string SanitizeCsvValue(string value)
    {
        return value.Replace("\r", " ").Replace("\n", " ");
    }

    private void AppendCsv(StringBuilder builder, string value, bool finalColumn = false)
    {
        if (value == null)
            value = string.Empty;

        value = SanitizeCsvValue(value);

        bool quote = value.Contains(",") || value.Contains("\"") || value.Contains("\n");
        if (quote)
            builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        else
            builder.Append(value);

        if (!finalColumn)
            builder.Append(',');
    }

    private void AppendCsv(StringBuilder builder, int value, bool finalColumn = false)
    {
        builder.Append(value.ToString(Invariant));

        if (!finalColumn)
            builder.Append(',');
    }

    private void AppendCsv(StringBuilder builder, float value, bool finalColumn = false)
    {
        builder.Append(value.ToString("0.###", Invariant));

        if (!finalColumn)
            builder.Append(',');
    }

    private void AppendCsv(StringBuilder builder, bool value, bool finalColumn = false)
    {
        builder.Append(value ? "true" : "false");

        if (!finalColumn)
            builder.Append(',');
    }
}
