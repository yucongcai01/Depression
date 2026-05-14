using System;
using UnityEngine;

public class CalibrationReadinessRecorder : MonoBehaviour
{
    [SerializeField] private CalibrationReadinessRecord currentRecord = new CalibrationReadinessRecord();

    public CalibrationReadinessRecord CurrentRecord => currentRecord;

    private void Awake()
    {
        StartNewSession();
    }

    public void StartNewSession()
    {
        currentRecord = new CalibrationReadinessRecord
        {
            sessionStartedAtUtc = DateTime.UtcNow.ToString("O")
        };
    }

    public void RecordAffectiveReadiness(CalibrationAffectiveReadinessData data)
    {
        currentRecord.affective = new CalibrationAffectiveReadinessRecord
        {
            completed = true,
            completedAtUtc = DateTime.UtcNow.ToString("O"),
            mood = data.mood,
            interest = data.interest,
            tension = data.tension,
            fatigue = data.fatigue,
            readinessScore = CalculateAffectiveReadinessScore(data)
        };
    }

    public void RecordPhysicalReadiness(bool trackingConfirmed)
    {
        currentRecord.physical.completed = true;
        currentRecord.physical.completedAtUtc = DateTime.UtcNow.ToString("O");
        currentRecord.physical.trackingConfirmed = trackingConfirmed;
        currentRecord.physical.readinessScore = CalculatePhysicalReadinessScore(currentRecord.physical);
    }

    public void SetHeartRate(float heartRateBpm)
    {
        currentRecord.physical.hasHeartRate = true;
        currentRecord.physical.heartRateBpm = Mathf.Max(0f, heartRateBpm);
        currentRecord.physical.readinessScore = CalculatePhysicalReadinessScore(currentRecord.physical);
    }

    public void SetHrvRmssd(float hrvRmssdMs)
    {
        currentRecord.physical.hasHrvRmssd = true;
        currentRecord.physical.hrvRmssdMs = Mathf.Max(0f, hrvRmssdMs);
        currentRecord.physical.readinessScore = CalculatePhysicalReadinessScore(currentRecord.physical);
    }

    public void RecordMotorCognitiveReadiness()
    {
        currentRecord.motorCognitive.completed = true;
        currentRecord.motorCognitive.completedAtUtc = DateTime.UtcNow.ToString("O");
        currentRecord.motorCognitive.readinessScore = CalculateMotorCognitiveReadinessScore(currentRecord.motorCognitive);
    }

    public void SetReactionTime(float reactionTimeMs)
    {
        currentRecord.motorCognitive.hasReactionTime = true;
        currentRecord.motorCognitive.reactionTimeMs = Mathf.Max(0f, reactionTimeMs);
        currentRecord.motorCognitive.readinessScore = CalculateMotorCognitiveReadinessScore(currentRecord.motorCognitive);
    }

    public void SetMovementAccuracy(float movementAccuracy)
    {
        currentRecord.motorCognitive.hasMovementAccuracy = true;
        currentRecord.motorCognitive.movementAccuracy = Mathf.Clamp01(movementAccuracy);
        currentRecord.motorCognitive.readinessScore = CalculateMotorCognitiveReadinessScore(currentRecord.motorCognitive);
    }

    public void FinalizeSession()
    {
        currentRecord.sessionCompletedAtUtc = DateTime.UtcNow.ToString("O");
        currentRecord.initialDifficultyBaseline = EstimateInitialDifficultyBaseline();
        CalibrationReadinessStore.Current = currentRecord;
    }

    public float EstimateInitialDifficultyBaseline()
    {
        float affective = currentRecord.affective.completed ? currentRecord.affective.readinessScore : 0.5f;
        float physical = currentRecord.physical.completed ? currentRecord.physical.readinessScore : 0.5f;
        float motorCognitive = currentRecord.motorCognitive.completed ? currentRecord.motorCognitive.readinessScore : 0.5f;

        return Mathf.Clamp01(0.4f * affective + 0.35f * physical + 0.25f * motorCognitive);
    }

    private float CalculateAffectiveReadinessScore(CalibrationAffectiveReadinessData data)
    {
        float lowMoodLoad = NormalizeLikert(data.mood);
        float interestReadiness = NormalizeLikert(data.interest);
        float tensionLoad = NormalizeLikert(data.tension);
        float fatigueLoad = NormalizeLikert(data.fatigue);

        float score = 0.25f * (1f - lowMoodLoad)
                    + 0.25f * interestReadiness
                    + 0.25f * (1f - tensionLoad)
                    + 0.25f * (1f - fatigueLoad);

        return Mathf.Clamp01(score);
    }

    private float CalculatePhysicalReadinessScore(CalibrationPhysicalReadinessRecord physical)
    {
        float score = physical.trackingConfirmed ? 0.7f : 0.25f;

        if (physical.hasHeartRate)
        {
            float heartRateScore = Mathf.InverseLerp(120f, 60f, physical.heartRateBpm);
            score = Mathf.Lerp(score, heartRateScore, 0.2f);
        }

        if (physical.hasHrvRmssd)
        {
            float hrvScore = Mathf.InverseLerp(15f, 80f, physical.hrvRmssdMs);
            score = Mathf.Lerp(score, hrvScore, 0.2f);
        }

        return Mathf.Clamp01(score);
    }

    private float CalculateMotorCognitiveReadinessScore(CalibrationMotorCognitiveReadinessRecord motorCognitive)
    {
        float score = 0.5f;

        if (motorCognitive.hasReactionTime)
        {
            float reactionScore = Mathf.InverseLerp(1200f, 250f, motorCognitive.reactionTimeMs);
            score = Mathf.Lerp(score, reactionScore, 0.35f);
        }

        if (motorCognitive.hasMovementAccuracy)
            score = Mathf.Lerp(score, motorCognitive.movementAccuracy, 0.5f);

        return Mathf.Clamp01(score);
    }

    private float NormalizeLikert(int value)
    {
        return Mathf.InverseLerp(1f, 5f, value);
    }
}

[Serializable]
public class CalibrationReadinessRecord
{
    public string sessionStartedAtUtc;
    public string sessionCompletedAtUtc;
    public CalibrationAffectiveReadinessRecord affective = new CalibrationAffectiveReadinessRecord();
    public CalibrationPhysicalReadinessRecord physical = new CalibrationPhysicalReadinessRecord();
    public CalibrationMotorCognitiveReadinessRecord motorCognitive = new CalibrationMotorCognitiveReadinessRecord();
    public float initialDifficultyBaseline;
}

[Serializable]
public struct CalibrationAffectiveReadinessData
{
    public int mood;
    public int interest;
    public int tension;
    public int fatigue;
}

[Serializable]
public class CalibrationAffectiveReadinessRecord
{
    public bool completed;
    public string completedAtUtc;
    public int mood;
    public int interest;
    public int tension;
    public int fatigue;
    [Range(0f, 1f)] public float readinessScore;
}

[Serializable]
public class CalibrationPhysicalReadinessRecord
{
    public bool completed;
    public string completedAtUtc;
    public bool trackingConfirmed;
    public bool hasHeartRate;
    public float heartRateBpm;
    public bool hasHrvRmssd;
    public float hrvRmssdMs;
    [Range(0f, 1f)] public float readinessScore;
}

[Serializable]
public class CalibrationMotorCognitiveReadinessRecord
{
    public bool completed;
    public string completedAtUtc;
    public bool hasReactionTime;
    public float reactionTimeMs;
    public bool hasMovementAccuracy;
    [Range(0f, 1f)] public float movementAccuracy;
    [Range(0f, 1f)] public float readinessScore;
}

public static class CalibrationReadinessStore
{
    public static CalibrationReadinessRecord Current;
}
