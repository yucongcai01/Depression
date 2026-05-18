using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum AdaptiveDifficultyMode
{
    PhysicalOnly,
    EmotionOnly,
    NoAdaptive
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
    public AdaptiveDifficultyMode adaptiveMode = AdaptiveDifficultyMode.PhysicalOnly;

    [Header("Fuzzy Systems")]
    public MamdaniFuzzySystem fuzzySystem;
    public MamdaniFuzzySystem emotionFuzzySystem;

    [Header("Difficulty Settings")]
    public float accuracyInput = 0f;
    public float accuracyChangeInput = 0f;
    [Range(-1f, 1f)] public float valenceInput = 0f;
    [Range(0f, 1f)] public float arousalInput = 0f;

    [Header("Speed Settings")]
    public float baseSpeedMultiplier = 1f;
    public float minSpeedMultiplier = RecommendedMinSpeedMultiplier;
    public float maxSpeedMultiplier = RecommendedMaxSpeedMultiplier;
    public float speedSmoothing = RecommendedSpeedSmoothing;

    [Header("Output (Read Only)")]
    public float targetSpeedMultiplier = 1f;
    public float adjustedSpeedMultiplier = 1f;
    public float fuzzyOutput = 1f;
    public bool emotionInputAvailable;

    private float previousAccuracy = 0f;
    private string statusMessage = string.Empty;

    private void Awake()
    {
        NormalizeSpeedSettings();
        ResolveReferences(includeEmotionProvider: true);
    }

    private void Start()
    {
        NormalizeSpeedSettings();
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
        ResolveReferences(includeEmotionProvider: adaptiveMode == AdaptiveDifficultyMode.EmotionOnly);

        if (dancePlayback == null)
        {
            statusMessage = "Missing DancePlayback";
            UpdateDebugText();
            return;
        }

        if (!dancePlayback.IsPlaying)
        {
            ApplyBaseSpeed(immediate: true);
            previousAccuracy = 0f;
            statusMessage = "Playback idle";
            UpdateDebugText();
            return;
        }

        switch (adaptiveMode)
        {
            case AdaptiveDifficultyMode.EmotionOnly:
                ApplyAdaptiveSpeed(EvaluateEmotionTargetSpeed());
                break;
            case AdaptiveDifficultyMode.NoAdaptive:
                ApplyBaseSpeed(immediate: true);
                statusMessage = "Adaptive disabled";
                break;
            case AdaptiveDifficultyMode.PhysicalOnly:
            default:
                ApplyAdaptiveSpeed(EvaluatePhysicalTargetSpeed());
                break;
        }

        UpdateDebugText();
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

    public void SetAdaptiveMode(AdaptiveDifficultyMode mode)
    {
        adaptiveMode = mode;
        ResolveReferences(includeEmotionProvider: adaptiveMode == AdaptiveDifficultyMode.EmotionOnly);
        ResetAdaptiveState();

        if (adaptiveMode == AdaptiveDifficultyMode.NoAdaptive)
            ApplyBaseSpeed(immediate: true);
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
        emotionInputAvailable = false;

        if (accuracyChecker == null)
        {
            statusMessage = "Missing AccuracyChecker";
            fuzzyOutput = baseSpeedMultiplier;
            return baseSpeedMultiplier;
        }

        if (fuzzySystem == null)
            fuzzySystem = CreatePhysicalFuzzySystem();

        float currentAccuracy = Mathf.Clamp01(accuracyChecker.currentAccuracy);
        float accChange = Mathf.Clamp(currentAccuracy - previousAccuracy, -0.08f, 0.08f);
        previousAccuracy = currentAccuracy;

        accuracyInput = currentAccuracy;
        accuracyChangeInput = accChange;

        var inputs = new Dictionary<string, float>
        {
            { "Accuracy", accuracyInput },
            { "AccuracyChange", accuracyChangeInput }
        };

        fuzzyOutput = fuzzySystem.Evaluate(inputs);
        statusMessage = "Physical adaptive";
        return fuzzyOutput;
    }

    private float EvaluateEmotionTargetSpeed()
    {
        if (faceEmotionProvider == null)
        {
            emotionInputAvailable = false;
            statusMessage = "Missing FaceEmotionProvider";
            fuzzyOutput = baseSpeedMultiplier;
            return baseSpeedMultiplier;
        }

        if (emotionFuzzySystem == null)
            emotionFuzzySystem = CreateEmotionFuzzySystem();

        emotionInputAvailable = faceEmotionProvider.TryGetAffectiveState(out float valence, out float arousal);
        if (!emotionInputAvailable)
        {
            statusMessage = "Waiting for emotion input";
            fuzzyOutput = baseSpeedMultiplier;
            return baseSpeedMultiplier;
        }

        valenceInput = Mathf.Clamp(valence, -1f, 1f);
        arousalInput = Mathf.Clamp01(arousal);

        var inputs = new Dictionary<string, float>
        {
            { "Valence", valenceInput },
            { "Arousal", arousalInput }
        };

        fuzzyOutput = emotionFuzzySystem.Evaluate(inputs);
        statusMessage = "Emotion adaptive";
        return fuzzyOutput;
    }

    private void ApplyAdaptiveSpeed(float targetSpeedMultiplier)
    {
        float target = ClampSpeed(targetSpeedMultiplier);
        this.targetSpeedMultiplier = target;
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
            ApplyAdaptiveSpeed(target);
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

        previousAccuracy = accuracyChecker != null ? Mathf.Clamp01(accuracyChecker.currentAccuracy) : 0f;
        fuzzyOutput = adjustedSpeedMultiplier;
        emotionInputAvailable = false;
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
            $"Emotion Available: {emotionInputAvailable}\n" +
            $"Fuzzy Output: {fuzzyOutput:F2}\n" +
            $"Target Speed: {targetSpeedMultiplier:F2}\n" +
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
