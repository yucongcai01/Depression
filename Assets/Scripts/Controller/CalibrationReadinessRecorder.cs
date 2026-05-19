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

        currentRecord.skills = new CalibrationSkillsReadinessRecord
        {
            completed = true,
            completedAtUtc = DateTime.UtcNow.ToString("O"),
            exergameExperience = data.exergameExperience,
            danceGameExperience = data.danceGameExperience,
            readinessScore = CalculateSkillsReadinessScore(data)
        };
    }

    public void RecordPhysicalReadiness(bool trackingConfirmed)
    {
        currentRecord.physical.completed = true;
        currentRecord.physical.completedAtUtc = DateTime.UtcNow.ToString("O");
        currentRecord.physical.trackingConfirmed = trackingConfirmed;
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
        float skills = currentRecord.skills.completed ? currentRecord.skills.readinessScore : 0.5f;
        float motorCognitive = currentRecord.motorCognitive.completed ? currentRecord.motorCognitive.readinessScore : 0.5f;

        return Mathf.Clamp01(0.4f * affective + 0.3f * skills + 0.3f * motorCognitive);
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

    private float CalculateSkillsReadinessScore(CalibrationAffectiveReadinessData data)
    {
        float exergameReadiness = NormalizeLikert(data.exergameExperience);
        float danceGameReadiness = NormalizeLikert(data.danceGameExperience);
        return Mathf.Clamp01(0.5f * exergameReadiness + 0.5f * danceGameReadiness);
    }

    private float CalculatePhysicalReadinessScore(CalibrationPhysicalReadinessRecord physical)
    {
        return physical.trackingConfirmed ? 0.7f : 0.25f;
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
    public CalibrationSkillsReadinessRecord skills = new CalibrationSkillsReadinessRecord();
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
    public int exergameExperience;
    public int danceGameExperience;
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
public class CalibrationSkillsReadinessRecord
{
    public bool completed;
    public string completedAtUtc;
    public int exergameExperience;
    public int danceGameExperience;
    [Range(0f, 1f)] public float readinessScore;
}

[Serializable]
public class CalibrationPhysicalReadinessRecord
{
    public bool completed;
    public string completedAtUtc;
    public bool trackingConfirmed;
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
