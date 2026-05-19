using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum AdaptiveDifficultyMode
{
    PhysicalOnly,
    EmotionOnly,
    NoAdaptive,
    PhysicalAndEmotion
}

public class AdaptiveDifficultyController : MonoBehaviour
{
    private const float RecommendedMinSpeedMultiplier = 0.85f;
    private const float RecommendedMaxSpeedMultiplier = 1.25f;
    private const float RecommendedSpeedSmoothing = 1.4f;

    [Header("References")]
    public AccuracyChecker accuracyChecker;
    public DancePlayback dancePlayback;
    public FaceEmotionProvider faceEmotionProvider;
    public TMP_Text debugText;

    [Header("Adaptive Mode")]
    public AdaptiveDifficultyMode adaptiveMode = AdaptiveDifficultyMode.PhysicalAndEmotion;

    [Header("Fuzzy Systems")]
    public MamdaniFuzzySystem fuzzySystem;
    public MamdaniFuzzySystem emotionFuzzySystem;

    [Header("Difficulty Settings")]
    [Min(0.5f)] public float difficultyUpdateInterval = 5f;
    public bool evaluateImmediatelyWhenPlaybackStarts = false;
    [Range(0f, 1f)] public float emotionWeight = 0.35f;
    public float accuracyInput = 0f;
    public float accuracyChangeInput = 0f;
    [Range(-1f, 1f)] public float valenceInput = 0f;
    [Range(0f, 1f)] public float arousalInput = 0f;

    [Header("Speed Settings")]
    public float baseSpeedMultiplier = 1f;
    public float minSpeedMultiplier = RecommendedMinSpeedMultiplier;
    public float maxSpeedMultiplier = RecommendedMaxSpeedMultiplier;
    public float speedSmoothing = RecommendedSpeedSmoothing;

    [Header("Calibration Initial Difficulty")]
    public bool useCalibrationInitialDifficulty = true;
    [Range(0f, 1f)] public float calibrationReadinessBaseline = 0.5f;
    public bool calibrationBaselineApplied;

    [Header("Output (Read Only)")]
    public float targetSpeedMultiplier = 1f;
    public float adjustedSpeedMultiplier = 1f;
    public float fuzzyOutput = 1f;
    public float physicalSpeedOutput = 1f;
    public float emotionSpeedOutput = 1f;
    public bool physicalInputAvailable;
    public bool emotionInputAvailable;
    public float lastDifficultyUpdatedAt = -1f;

    private float previousAccuracy = 0f;
    private bool hasPreviousAccuracyWindow;
    private float accuracySampleSum;
    private float accuracySampleTime;
    private float nextDifficultyUpdateTime;
    private bool wasPlaybackPlaying;
    private string statusMessage = string.Empty;

    private void Awake()
    {
        NormalizeSpeedSettings();
        ResolveReferences(includeEmotionProvider: true);
    }

    private void Start()
    {
        NormalizeSpeedSettings();
        ApplyCalibrationInitialDifficulty();
        fuzzySystem = CreatePhysicalFuzzySystem();
        emotionFuzzySystem = CreateEmotionFuzzySystem();
        ResetAdaptiveState();
    }

    private void OnValidate()
    {
        NormalizeSpeedSettings();
    }

    private void Update()
    {
        ResolveReferences(includeEmotionProvider: UsesEmotionInput());

        if (dancePlayback == null)
        {
            statusMessage = "Missing DancePlayback";
            UpdateDebugText();
            return;
        }

        if (adaptiveMode == AdaptiveDifficultyMode.NoAdaptive)
        {
            ApplyBaseSpeed(immediate: !dancePlayback.IsPlaying);
            statusMessage = "Adaptive disabled";
            UpdateDebugText();
            return;
        }

        if (!dancePlayback.IsPlaying)
        {
            ApplyBaseSpeed(immediate: true);
            wasPlaybackPlaying = false;
            ResetPerformanceWindow();
            statusMessage = "Playback idle";
            UpdateDebugText();
            return;
        }

        if (!wasPlaybackPlaying)
        {
            wasPlaybackPlaying = true;
            ResetPerformanceWindow();
            nextDifficultyUpdateTime = Time.time + (evaluateImmediatelyWhenPlaybackStarts ? 0f : difficultyUpdateInterval);
            targetSpeedMultiplier = ClampSpeed(baseSpeedMultiplier);
        }

        SamplePerformanceWindow();

        if (Time.time >= nextDifficultyUpdateTime)
        {
            UpdateDifficultyTarget();
            nextDifficultyUpdateTime = Time.time + Mathf.Max(0.5f, difficultyUpdateInterval);
        }

        MovePlaybackSpeedTowardTarget();
        UpdateDebugText();
    }

    public void ForceDifficultyUpdate()
    {
        UpdateDifficultyTarget();
        nextDifficultyUpdateTime = Time.time + Mathf.Max(0.5f, difficultyUpdateInterval);
    }

    public void ResetAdaptiveStateForNewAction()
    {
        ResetPerformanceWindow();
        nextDifficultyUpdateTime = Time.time + Mathf.Max(0.5f, difficultyUpdateInterval);
    }

    private void UpdateDifficultyTarget()
    {
        float nextTarget;

        switch (adaptiveMode)
        {
            case AdaptiveDifficultyMode.EmotionOnly:
                nextTarget = EvaluateEmotionTargetSpeed();
                break;
            case AdaptiveDifficultyMode.PhysicalAndEmotion:
                nextTarget = EvaluateCombinedTargetSpeed();
                break;
            case AdaptiveDifficultyMode.PhysicalOnly:
            default:
                nextTarget = EvaluatePhysicalTargetSpeed();
                break;
        }

        targetSpeedMultiplier = ClampSpeed(nextTarget);
        fuzzyOutput = targetSpeedMultiplier;
        lastDifficultyUpdatedAt = Time.time;
        ResetPerformanceWindow(keepPreviousAccuracy: true);
    }

    public void SetEmotionOnlyMode()
    {
        SetAdaptiveMode(AdaptiveDifficultyMode.EmotionOnly);
    }

    public void SetPhysicalOnlyMode()
    {
        SetAdaptiveMode(AdaptiveDifficultyMode.PhysicalOnly);
    }

    public void SetNoAdaptiveMode()
    {
        SetAdaptiveMode(AdaptiveDifficultyMode.NoAdaptive);
    }

    public void SetPhysicalAndEmotionMode()
    {
        SetAdaptiveMode(AdaptiveDifficultyMode.PhysicalAndEmotion);
    }

    public void SetAdaptiveMode(AdaptiveDifficultyMode mode)
    {
        adaptiveMode = mode;
        ResolveReferences(includeEmotionProvider: UsesEmotionInput());
        ResetAdaptiveState();

        if (adaptiveMode == AdaptiveDifficultyMode.NoAdaptive)
            ApplyBaseSpeed(immediate: true);
    }

    private bool UsesEmotionInput()
    {
        return adaptiveMode == AdaptiveDifficultyMode.EmotionOnly ||
               adaptiveMode == AdaptiveDifficultyMode.PhysicalAndEmotion;
    }

    private void ResolveReferences(bool includeEmotionProvider = false)
    {
        if (accuracyChecker == null)
            accuracyChecker = FindAnyObjectByType<AccuracyChecker>();

        if (dancePlayback == null)
            dancePlayback = FindAnyObjectByType<DancePlayback>();

        if (includeEmotionProvider && faceEmotionProvider == null)
            faceEmotionProvider = FindAnyObjectByType<FaceEmotionProvider>();
    }

    private float EvaluatePhysicalTargetSpeed()
    {
        if (accuracyChecker == null)
        {
            physicalInputAvailable = false;
            statusMessage = "Missing AccuracyChecker";
            physicalSpeedOutput = baseSpeedMultiplier;
            return baseSpeedMultiplier;
        }

        if (fuzzySystem == null)
            fuzzySystem = CreatePhysicalFuzzySystem();

        float currentAccuracy = GetWindowAccuracy();
        float accChange = hasPreviousAccuracyWindow
            ? Mathf.Clamp(currentAccuracy - previousAccuracy, -0.08f, 0.08f)
            : 0f;

        physicalInputAvailable = true;
        accuracyInput = currentAccuracy;
        accuracyChangeInput = accChange;
        previousAccuracy = currentAccuracy;
        hasPreviousAccuracyWindow = true;

        var inputs = new Dictionary<string, float>
        {
            { "Accuracy", accuracyInput },
            { "AccuracyChange", accuracyChangeInput }
        };

        physicalSpeedOutput = fuzzySystem.Evaluate(inputs);
        statusMessage = "Physical adaptive";
        return physicalSpeedOutput;
    }

    private float EvaluateEmotionTargetSpeed()
    {
        if (faceEmotionProvider == null)
        {
            emotionInputAvailable = false;
            statusMessage = "Missing FaceEmotionProvider";
            emotionSpeedOutput = baseSpeedMultiplier;
            return baseSpeedMultiplier;
        }

        if (emotionFuzzySystem == null)
            emotionFuzzySystem = CreateEmotionFuzzySystem();

        emotionInputAvailable = faceEmotionProvider.TryGetAffectiveState(out float valence, out float arousal);
        if (!emotionInputAvailable)
        {
            statusMessage = "Waiting for emotion input";
            emotionSpeedOutput = baseSpeedMultiplier;
            return baseSpeedMultiplier;
        }

        valenceInput = Mathf.Clamp(valence, -1f, 1f);
        arousalInput = Mathf.Clamp01(arousal);

        var inputs = new Dictionary<string, float>
        {
            { "Valence", valenceInput },
            { "Arousal", arousalInput }
        };

        emotionSpeedOutput = emotionFuzzySystem.Evaluate(inputs);
        statusMessage = "Emotion adaptive";
        return emotionSpeedOutput;
    }

    private float EvaluateCombinedTargetSpeed()
    {
        float physicalTarget = EvaluatePhysicalTargetSpeed();
        float emotionTarget = EvaluateEmotionTargetSpeed();

        if (physicalInputAvailable && emotionInputAvailable)
        {
            statusMessage = "Physical + emotion adaptive";
            return Mathf.Lerp(physicalTarget, emotionTarget, emotionWeight);
        }

        if (emotionInputAvailable)
        {
            statusMessage = "Emotion adaptive (missing physical)";
            return emotionTarget;
        }

        statusMessage = faceEmotionProvider == null
            ? "Physical adaptive (missing emotion)"
            : "Physical adaptive (waiting for emotion)";
        return physicalTarget;
    }

    private void MovePlaybackSpeedTowardTarget()
    {
        float target = ClampSpeed(targetSpeedMultiplier);
        float alpha = 1f - Mathf.Exp(-Mathf.Max(0.01f, speedSmoothing) * Time.deltaTime);
        adjustedSpeedMultiplier = Mathf.Lerp(adjustedSpeedMultiplier, target, alpha);
        dancePlayback.speedMultiplier = ClampSpeed(adjustedSpeedMultiplier);
    }

    private void ApplyBaseSpeed(bool immediate)
    {
        float target = ClampSpeed(baseSpeedMultiplier);
        targetSpeedMultiplier = target;

        if (immediate)
        {
            adjustedSpeedMultiplier = target;
            if (dancePlayback != null)
                dancePlayback.speedMultiplier = target;
        }
        else
        {
            MovePlaybackSpeedTowardTarget();
        }

        fuzzyOutput = target;
    }

    private void NormalizeSpeedSettings()
    {
        if (baseSpeedMultiplier <= 0f)
            baseSpeedMultiplier = 1f;

        if (minSpeedMultiplier <= 0f || minSpeedMultiplier >= baseSpeedMultiplier)
            minSpeedMultiplier = Mathf.Min(RecommendedMinSpeedMultiplier, baseSpeedMultiplier - 0.01f);

        if (maxSpeedMultiplier <= baseSpeedMultiplier)
            maxSpeedMultiplier = Mathf.Max(RecommendedMaxSpeedMultiplier, baseSpeedMultiplier + 0.01f);

        if (speedSmoothing <= 0f)
            speedSmoothing = RecommendedSpeedSmoothing;
    }

    private void ApplyCalibrationInitialDifficulty()
    {
        calibrationBaselineApplied = false;

        if (!useCalibrationInitialDifficulty || CalibrationReadinessStore.Current == null)
            return;

        calibrationReadinessBaseline = Mathf.Clamp01(CalibrationReadinessStore.Current.initialDifficultyBaseline);
        baseSpeedMultiplier = MapReadinessToInitialSpeed(calibrationReadinessBaseline);
        NormalizeSpeedSettings();
        calibrationBaselineApplied = true;
    }

    private float MapReadinessToInitialSpeed(float readiness)
    {
        float min = Mathf.Min(minSpeedMultiplier, maxSpeedMultiplier);
        float max = Mathf.Max(minSpeedMultiplier, maxSpeedMultiplier);
        float neutral = Mathf.Clamp(baseSpeedMultiplier, min, max);

        return readiness < 0.5f
            ? Mathf.Lerp(min, neutral, readiness / 0.5f)
            : Mathf.Lerp(neutral, max, (readiness - 0.5f) / 0.5f);
    }

    private float ClampSpeed(float value)
    {
        float min = Mathf.Min(minSpeedMultiplier, maxSpeedMultiplier);
        float max = Mathf.Max(minSpeedMultiplier, maxSpeedMultiplier);
        return Mathf.Clamp(value, min, max);
    }

    private void ResetAdaptiveState()
    {
        adjustedSpeedMultiplier = dancePlayback != null
            ? ClampSpeed(dancePlayback.speedMultiplier)
            : ClampSpeed(baseSpeedMultiplier);

        targetSpeedMultiplier = ClampSpeed(baseSpeedMultiplier);
        previousAccuracy = accuracyChecker != null ? Mathf.Clamp01(accuracyChecker.currentAccuracy) : 0f;
        fuzzyOutput = adjustedSpeedMultiplier;
        physicalSpeedOutput = adjustedSpeedMultiplier;
        emotionSpeedOutput = adjustedSpeedMultiplier;
        physicalInputAvailable = false;
        emotionInputAvailable = false;
        wasPlaybackPlaying = false;
        lastDifficultyUpdatedAt = -1f;
        ResetPerformanceWindow();
    }

    private void SamplePerformanceWindow()
    {
        if (accuracyChecker == null)
            return;

        float deltaTime = Mathf.Max(0f, Time.deltaTime);
        accuracySampleSum += Mathf.Clamp01(accuracyChecker.currentAccuracy) * deltaTime;
        accuracySampleTime += deltaTime;
    }

    private float GetWindowAccuracy()
    {
        if (accuracySampleTime > 0f)
            return Mathf.Clamp01(accuracySampleSum / accuracySampleTime);

        return accuracyChecker != null ? Mathf.Clamp01(accuracyChecker.currentAccuracy) : 0.5f;
    }

    private void ResetPerformanceWindow(bool keepPreviousAccuracy = false)
    {
        accuracySampleSum = 0f;
        accuracySampleTime = 0f;

        if (!keepPreviousAccuracy)
        {
            hasPreviousAccuracyWindow = false;
            previousAccuracy = accuracyChecker != null ? Mathf.Clamp01(accuracyChecker.currentAccuracy) : 0f;
        }
    }

    private void UpdateDebugText()
    {
        if (debugText == null)
            return;

        debugText.text =
            $"Mode: {adaptiveMode}\n" +
            $"Status: {statusMessage}\n" +
            $"Accuracy: {accuracyInput:F2}\n" +
            $"Accuracy Change: {accuracyChangeInput:F2}\n" +
            $"Valence: {valenceInput:F2}\n" +
            $"Arousal: {arousalInput:F2}\n" +
            $"Physical Available: {physicalInputAvailable}\n" +
            $"Emotion Available: {emotionInputAvailable}\n" +
            $"Calibration Baseline: {(calibrationBaselineApplied ? calibrationReadinessBaseline.ToString("F2") : "None")}\n" +
            $"Physical Output: {physicalSpeedOutput:F2}\n" +
            $"Emotion Output: {emotionSpeedOutput:F2}\n" +
            $"Combined Output: {fuzzyOutput:F2}\n" +
            $"Target Speed: {targetSpeedMultiplier:F2}\n" +
            $"Update Interval: {difficultyUpdateInterval:F1}s\n" +
            $"Last Update: {(lastDifficultyUpdatedAt >= 0f ? lastDifficultyUpdatedAt.ToString("F1") : "None")}\n" +
            $"Playback Speed: {(dancePlayback != null ? dancePlayback.speedMultiplier : 0f):F2}";
    }

    private MamdaniFuzzySystem CreatePhysicalFuzzySystem()
    {
        var system = new MamdaniFuzzySystem();

        system.inputVariables.Add(new FuzzyVariable
        {
            name = "Accuracy",
            minValue = 0f,
            maxValue = 1f,
            sets = new List<FuzzySet>
            {
                new FuzzySet { name = "Low", type = FuzzySetType.Trapezoid, a = -0.01f, b = 0f, c = 0.4f, d = 0.58f },
                new FuzzySet { name = "Medium", type = FuzzySetType.Triangle, a = 0.45f, b = 0.68f, c = 0.82f },
                new FuzzySet { name = "High", type = FuzzySetType.Trapezoid, a = 0.72f, b = 0.84f, c = 1f, d = 1.01f }
            }
        });

        system.inputVariables.Add(new FuzzyVariable
        {
            name = "AccuracyChange",
            minValue = -0.08f,
            maxValue = 0.08f,
            sets = new List<FuzzySet>
            {
                new FuzzySet { name = "Decreasing", type = FuzzySetType.Triangle, a = -0.081f, b = -0.08f, c = 0f },
                new FuzzySet { name = "Stable", type = FuzzySetType.Triangle, a = -0.035f, b = 0f, c = 0.035f },
                new FuzzySet { name = "Increasing", type = FuzzySetType.Triangle, a = 0f, b = 0.08f, c = 0.081f }
            }
        });

        system.outputVariable = CreateSpeedOutputVariable();

        system.rules = new List<FuzzyRule>
        {
            CreateRule("Accuracy", "Low", "AccuracyChange", "Decreasing", "Slow"),
            CreateRule("Accuracy", "Low", "AccuracyChange", "Stable", "SlightlySlow"),
            CreateRule("Accuracy", "Low", "AccuracyChange", "Increasing", "Normal"),
            CreateRule("Accuracy", "Medium", "AccuracyChange", "Decreasing", "Normal"),
            CreateRule("Accuracy", "Medium", "AccuracyChange", "Stable", "SlightlyFast"),
            CreateRule("Accuracy", "Medium", "AccuracyChange", "Increasing", "Fast"),
            CreateRule("Accuracy", "High", "AccuracyChange", "Decreasing", "SlightlyFast"),
            CreateRule("Accuracy", "High", "AccuracyChange", "Stable", "Fast"),
            CreateRule("Accuracy", "High", "AccuracyChange", "Increasing", "Fast")
        };

        return system;
    }

    private MamdaniFuzzySystem CreateEmotionFuzzySystem()
    {
        var system = new MamdaniFuzzySystem();

        system.inputVariables.Add(new FuzzyVariable
        {
            name = "Valence",
            minValue = -1f,
            maxValue = 1f,
            sets = new List<FuzzySet>
            {
                new FuzzySet { name = "Negative", type = FuzzySetType.Trapezoid, a = -1.01f, b = -1f, c = -0.18f, d = 0.04f },
                new FuzzySet { name = "Neutral", type = FuzzySetType.Triangle, a = -0.12f, b = 0f, c = 0.18f },
                new FuzzySet { name = "Positive", type = FuzzySetType.Trapezoid, a = 0.08f, b = 0.25f, c = 1f, d = 1.01f }
            }
        });

        system.inputVariables.Add(new FuzzyVariable
        {
            name = "Arousal",
            minValue = 0f,
            maxValue = 1f,
            sets = new List<FuzzySet>
            {
                new FuzzySet { name = "Calm", type = FuzzySetType.Trapezoid, a = -0.01f, b = 0f, c = 0.12f, d = 0.25f },
                new FuzzySet { name = "Engaged", type = FuzzySetType.Triangle, a = 0.15f, b = 0.35f, c = 0.58f },
                new FuzzySet { name = "High", type = FuzzySetType.Trapezoid, a = 0.48f, b = 0.68f, c = 1f, d = 1.01f }
            }
        });

        system.outputVariable = CreateSpeedOutputVariable();

        system.rules = new List<FuzzyRule>
        {
            CreateRule("Valence", "Negative", "Arousal", "Calm", "SlightlySlow"),
            CreateRule("Valence", "Negative", "Arousal", "Engaged", "Slow"),
            CreateRule("Valence", "Negative", "Arousal", "High", "Slow"),
            CreateRule("Valence", "Neutral", "Arousal", "Calm", "SlightlySlow"),
            CreateRule("Valence", "Neutral", "Arousal", "Engaged", "Normal"),
            CreateRule("Valence", "Neutral", "Arousal", "High", "Normal"),
            CreateRule("Valence", "Positive", "Arousal", "Calm", "Normal"),
            CreateRule("Valence", "Positive", "Arousal", "Engaged", "SlightlyFast"),
            CreateRule("Valence", "Positive", "Arousal", "High", "Fast")
        };

        return system;
    }

    private FuzzyVariable CreateSpeedOutputVariable()
    {
        float min = Mathf.Min(minSpeedMultiplier, maxSpeedMultiplier);
        float max = Mathf.Max(minSpeedMultiplier, maxSpeedMultiplier);
        float normal = Mathf.Clamp(baseSpeedMultiplier, min, max);
        float slightlySlow = Mathf.Lerp(min, normal, 0.5f);
        float slightlyFast = Mathf.Lerp(normal, max, 0.5f);

        return new FuzzyVariable
        {
            name = "SpeedMultiplier",
            minValue = min,
            maxValue = max,
            sets = new List<FuzzySet>
            {
                new FuzzySet { name = "Slow", type = FuzzySetType.Triangle, a = min - 0.01f, b = min, c = slightlySlow },
                new FuzzySet { name = "SlightlySlow", type = FuzzySetType.Triangle, a = min, b = slightlySlow, c = normal },
                new FuzzySet { name = "Normal", type = FuzzySetType.Triangle, a = slightlySlow, b = normal, c = slightlyFast },
                new FuzzySet { name = "SlightlyFast", type = FuzzySetType.Triangle, a = normal, b = slightlyFast, c = max },
                new FuzzySet { name = "Fast", type = FuzzySetType.Triangle, a = slightlyFast, b = max, c = max + 0.01f }
            }
        };
    }

    private FuzzyRule CreateRule(string inputVariableA, string inputSetA, string inputVariableB, string inputSetB, string outputSet)
    {
        return new FuzzyRule
        {
            antecedents = new List<FuzzyRule.Condition>
            {
                new FuzzyRule.Condition { variableName = inputVariableA, setName = inputSetA },
                new FuzzyRule.Condition { variableName = inputVariableB, setName = inputSetB }
            },
            consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = outputSet }
        };
    }
}
