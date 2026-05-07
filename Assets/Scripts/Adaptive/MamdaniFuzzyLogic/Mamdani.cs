using UnityEngine;
using System;
using UnityEngine.LowLevelPhysics;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Debug = UnityEngine.Debug;

[Serializable]
public enum FuzzySetType
{
    Triangle, // 三角形隶属函数
    Trapezoid, // 梯形隶属函数
    Gaussian // 高斯隶属函数
}

[Serializable]
public class FuzzySet
{
    public string name;
    public FuzzySetType type;
    public float a, b, c, d; // 参数，三角形(a,b,c)，梯形(a,b,c,d)，高斯(a=中心，b=宽度)

    public float GetMembership(float x)
    {
        if (type == FuzzySetType.Triangle)
        {
            /// <summary>
            /// 三角形隶属函数
            /// a: 左端点，b: 顶点，c: 右端点
            /// 当 x <= a 或 x >= c 时，隶属度为 0
            /// 当 x == b 时，隶属度为 1
            /// 当 a < x < b 时，隶属度线性增加 (x - a) / (b - a)
            /// 当 b < x < c 时，隶属度线性减少 (c - x) / (c - b)
            /// </summary>
            if (x <= a || x >= c) return 0f;
            else if (x == b) return 1f;
            else if (x > a && x < b) return (x - a) / (b - a);
            else return (c - x) / (c - b);
        }
        else if (type == FuzzySetType.Trapezoid)
        {
            /// <summary>
            /// 梯形隶属函数
            /// a: 左端点，b: 左肩顶点，c: 右肩顶点，d: 右端点
            /// 当 x <= a 或 x >= d 时，隶属度为 0
            /// 当 b <= x <= c 时，隶属度为 1
            /// 当 a < x < b 时，隶属度线性增加 (x - a) / (b - a)
            /// 当 c < x < d 时，隶属度线性减少 (d - x) / (d - c)
            /// </summary>
            if (x <= a || x >= d) return 0f;
            else if (x >= b && x <= c) return 1f;
            else if (x > a && x < b) return (x - a) / (b - a);
            else return (d - x) / (d - c);
        }
        else // Gaussian
        {
            /// <summary>
            /// 高斯隶属函数
            /// a: 中心，b: 宽度
            /// 隶属度 = exp(-((x - a)^2) / (2 * b^2))
            /// </summary>
            float exponent = -Mathf.Pow(x - a, 2) / (2 * Mathf.Pow(b, 2));
            return Mathf.Exp(exponent);
        }
    }
}

[Serializable]
public class FuzzyVariable
{
    public string name;
    public float minValue;
    public float maxValue;
    public List<FuzzySet> sets = new List<FuzzySet>();

    /// <summary>
    /// 模糊化：将输入的精确值转换为每个模糊集的隶属度
    /// </summary>
    /// <param name="crispValue"></param>
    /// <returns></returns>
    public Dictionary<string, float> Fuzzify(float crispValue) // crispValue: 输入的精确值，返回每个模糊集的隶属度
    {
        var result = new Dictionary<string, float>();
        foreach (var set in sets)
        {
            result[set.name] = set.GetMembership(crispValue);
        }
        return result;
    }
}

[Serializable]
public class FuzzyRule
{
    // 前提条件：多个“变量名 is 集合名”的条件，使用AND连接，可扩展OR，例如:"Accuracy is Low AND Speed is Fast"
    public List<Condition> antecedents = new List<Condition>();
    // 结论：单一“变量名 is 集合名”的结果，例如 "Score is Bad"
    public Consequent consequent;

    [Serializable]
    public struct Condition
    {
        public string variableName; // 变量名，例如 "Accuracy"
        public string setName; // 模糊集名，例如 "Low"
    }

    [Serializable]
    public struct Consequent
    {
        public string variableName; // 变量名，例如 "Score"
        public string setName; // 模糊集名，例如 "Bad"
    }
}

[Serializable]
public class MamdaniFuzzySystem
{
    public List<FuzzyVariable> inputVariables = new List<FuzzyVariable>();
    public FuzzyVariable outputVariable; // 只有一个输出变量
    public List<FuzzyRule> rules = new List<FuzzyRule>();

    public float Evaluate(Dictionary<string, float> crispInputs) // 推理引擎，输入每个变量的精确值，输出去模糊化后的结果
    {
        // 1. 模糊化输入
        Dictionary<string, Dictionary<string, float>> fuzzifiedInputs = new Dictionary<string, Dictionary<string, float>>();
        foreach (var inputVar in inputVariables)
        {
            if (crispInputs.TryGetValue(inputVar.name, out float val))
                fuzzifiedInputs[inputVar.name] = inputVar.Fuzzify(val);
            else
                Debug.LogError($"Missing crisp input for variable '{inputVar.name}'");
        }

        // 2. 评估规则，得到每个输出模糊集的激活程度
        Dictionary<string, float> ruleStrengths = new Dictionary<string, float>(); // 用输出集合名聚合强度（取max）
        foreach (var rule in rules)
        {
            float strength = 1f;
            foreach (var cond in rule.antecedents)
            {
                if (fuzzifiedInputs.TryGetValue(cond.variableName, out var sets) && sets.TryGetValue(cond.setName, out float mem))
                    strength = Mathf.Min(strength, mem);
                else
                    strength = 0f;
            }
            if (strength > 0)
            {
                string outSet = rule.consequent.setName;
                if (ruleStrengths.ContainsKey(outSet))
                    ruleStrengths[outSet] = Mathf.Max(ruleStrengths[outSet], strength);
                else
                    ruleStrengths[outSet] = strength;
            }
        }

        // 3. 去模糊化：使用重心法计算输出的精确值
        const int samples = 100;
        float step = (outputVariable.maxValue - outputVariable.minValue) / samples;
        float numerator = 0f, denominator = 0f;

        for (int i = 0; i <= samples; i++)
        {
            float x = outputVariable.minValue + i * step;
            // 聚合隶属度：取所有规则对该x的最大隶属度（经过截断）
            float agg = 0f;
            foreach (var kv in ruleStrengths)
            {
                FuzzySet set = outputVariable.sets.FirstOrDefault(s => s.name == kv.Key);
                if (set != null)
                {
                    float m = set.GetMembership(x);
                    float cut = Mathf.Min(m, kv.Value); // Mamdani 蕴含采用 min(隶属度, 强度)
                    if (cut > agg) agg = cut;
                }
            }
            numerator += x * agg;
            denominator += agg;
        }

        return denominator == 0 ? outputVariable.minValue : numerator / denominator;
    }
}
