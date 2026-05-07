using System;
using UnityEngine;
using TMPro;

public class AdaptiveDifficultyController : MonoBehaviour
{
    [Header("References")]
    public AccuracyChecker accuracyChecker;
    public DancePlayback dancePlayback;
    public TMP_Text debugText; // 可选：用于显示调试信息

    [Header("Fuzzy System")]
    public MamdaniFuzzySystem fuzzySystem;

    [Header("Difficulty Settings")]
    public float accuracyInput = 0f;
    public float accuracyChangeInput = 0f;

    [Header("Output (Read Only)")]
    public float adjustedSpeedMultiplier = 1f;
    public float baseSpeedMultiplier = 1f; // 基础速度倍率，默认为 1（正常速度）

    private float previousAccuracy = 0f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InitializeFuzzySystem();
    }

    // Update is called once per frame
    void Update()
    {
        if (accuracyChecker == null || dancePlayback == null || !dancePlayback.IsPlaying)
        {
            Debug.LogWarning("AdaptiveDifficultyController: Missing references or playback not active.");
            return;
        }

        if (fuzzySystem == null)
        {
            Debug.LogWarning("AdaptiveDifficultyController: Fuzzy system not initialized.");
            return;
        }

        if (!dancePlayback.IsPlaying)
        {
            adjustedSpeedMultiplier = baseSpeedMultiplier; // 如果没有播放，保持基础速度
            previousAccuracy = 0f; // 重置之前的准确率
            return;
        }

        float currentAccuracy = accuracyChecker.currentAccuracy;
        float accChange = currentAccuracy - previousAccuracy;
        previousAccuracy = currentAccuracy;

        accuracyInput = currentAccuracy;
        accuracyChangeInput = accChange;

        var inputs = new System.Collections.Generic.Dictionary<string, float>
        {
            { "Accuracy", accuracyInput },
            { "AccuracyChange", accuracyChangeInput }
        };

        float fuzzyOutput = fuzzySystem.Evaluate(inputs);

        adjustedSpeedMultiplier = Mathf.Lerp(baseSpeedMultiplier, fuzzyOutput, Time.deltaTime * 3f); // 平滑过渡到新的速度倍率
        dancePlayback.speedMultiplier = Mathf.Clamp(adjustedSpeedMultiplier, 0.5f, 1.5f); // 限制速度倍率在合理范围内

        if (debugText != null)
        {
            debugText.text = $"Accuracy: {accuracyInput:F2}\n" +
                             $"Accuracy Change: {accuracyChangeInput:F2}\n" +
                             $"Fuzzy Output: {fuzzyOutput:F2}\n" +
                             $"Adjusted Speed Multiplier: {adjustedSpeedMultiplier:F2}\n" +
                             $"Current Playback Speed: {dancePlayback.speedMultiplier:F2}";
        }
    }

    void InitializeFuzzySystem()
    {
        // Initialization logic for the fuzzy system
        fuzzySystem = new MamdaniFuzzySystem();

        // 变量1：Accuracy，表示当前的准确率水平
        var accuracy = new FuzzyVariable
        {
            name = "Accuracy",
            minValue = 0f,
            maxValue = 1f,
            sets = new System.Collections.Generic.List<FuzzySet>
            {
                new FuzzySet { name = "Low", type = FuzzySetType.Triangle, a = 0f, b = 0f, c = 0.5f },
                new FuzzySet { name = "Medium", type = FuzzySetType.Triangle, a = 0.25f, b = 0.5f, c = 0.75f },
                new FuzzySet { name = "High", type = FuzzySetType.Triangle, a = 0.5f, b = 1f, c = 1f }
            }
        };
        fuzzySystem.inputVariables.Add(accuracy);

        // 变量2：AccuracyChange，表示当前准确率与上一帧准确率的变化
        var accuracyChange = new FuzzyVariable
        {
            name = "AccuracyChange",
            minValue = -1f,
            maxValue = 1f,
            sets = new System.Collections.Generic.List<FuzzySet>
            {
                new FuzzySet { name = "Decreasing", type = FuzzySetType.Triangle, a = -1f, b = -1f, c = 0f },
                new FuzzySet { name = "Stable", type = FuzzySetType.Triangle, a = -0.5f, b = 0f, c = 0.5f },
                new FuzzySet { name = "Increasing", type = FuzzySetType.Triangle, a = 0f, b = 1f, c = 1f }
            }
        };
        fuzzySystem.inputVariables.Add(accuracyChange);

        // 输出变量：SpeedMultiplier，表示调整后的速度倍率
        var speedMultiplier = new FuzzyVariable
        {
            name = "SpeedMultiplier",
            minValue = 0.5f,
            maxValue = 1.5f,
            sets = new System.Collections.Generic.List<FuzzySet>
            {
                new FuzzySet { name = "Slow", type = FuzzySetType.Triangle, a = 0.5f, b = 0.5f, c = 1f },
                new FuzzySet { name = "Normal", type = FuzzySetType.Triangle, a = 0.75f, b = 1f, c = 1.25f },
                new FuzzySet { name = "Fast", type = FuzzySetType.Triangle, a = 1f, b = 1.5f, c = 1.5f }
            }
        };
        fuzzySystem.outputVariable = speedMultiplier;

        // Rules
        fuzzySystem.rules = new System.Collections.Generic.List<FuzzyRule>
        {
            // 规则1：如果准确率低且正在下降，则减慢速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "Low" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Decreasing" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Slow" }
            },
            // 规则2：如果准确率低但稳定，则略微减慢速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "Low" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Stable" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Normal" }
            },
            // 规则3：如果准确率低但正在上升，则保持正常速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "Low" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Increasing" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Normal" }
            },
            // 规则4：如果准确率中等且正在下降，则略微减慢速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "Medium" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Decreasing" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Normal" }
            },
            // 规则5：如果准确率中等且稳定，则保持正常速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "Medium" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Stable" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Normal" }
            },
            // 规则6：如果准确率中等但正在上升，则略微加快速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "Medium" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Increasing" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Fast" }
            },
            // 规则7：如果准确率高但正在下降，则略微减慢速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "High" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Decreasing" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Normal" }
            },
            // 规则8：如果准确率高且稳定，则略微加快速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "High" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Stable" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Fast" }
            },
            // 规则9：如果准确率高且正在上升，则加快速度
            new FuzzyRule
            {
                antecedents = new System.Collections.Generic.List<FuzzyRule.Condition>
                {
                    new FuzzyRule.Condition { variableName = "Accuracy", setName = "High" },
                    new FuzzyRule.Condition { variableName = "AccuracyChange", setName = "Increasing" }
                },
                consequent = new FuzzyRule.Consequent { variableName = "SpeedMultiplier", setName = "Fast" }
            }
        };
    }
}
