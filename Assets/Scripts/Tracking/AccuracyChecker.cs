using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using TMPro;

public class AccuracyChecker : MonoBehaviour
{
    [Header("References")]
    public NewBodyTracker playerTracker;
    public DancePlayback referencePlayback;

    [Header("Mirror Correction")]
    public bool legacyExtraMirrorLiveLandmarks = false;

    [Header("Scoring Settings")]
    public float maxDistanceForZeroScore = 0.5f;
    public bool useProcrustesAlignment = false;

    [Header("UI Settings")]
    public TMP_Text accuracyText;

    [Header("Output (Read Only)")]
    public float currentAccuracy = 0f;

    private int[] validIndices;

    void Start()
    {
        if (playerTracker == null) playerTracker = FindAnyObjectByType<NewBodyTracker>();
        if (referencePlayback == null) referencePlayback = FindAnyObjectByType<DancePlayback>();

        RefreshValidIndices();
    }

    void Update()
    {
        currentAccuracy = 0f;

        if (playerTracker == null || referencePlayback == null || !referencePlayback.IsPlaying)
        {
            UpdateUI();
            return;
        }

        var liveLandmarks = playerTracker.currentLandmarksExternal;
        var clipLandmarks = referencePlayback.GetCurrentLandmarks();

        if (liveLandmarks == null || liveLandmarks.Count < 33 ||
            clipLandmarks == null || clipLandmarks.Count < 33)
        {
            UpdateUI();
            return;
        }

        if (validIndices == null || validIndices.Length == 0)
            RefreshValidIndices();

        List<Vector3> livePoints = new List<Vector3>();
        List<Vector3> clipPoints = new List<Vector3>();

        foreach (int idx in validIndices)
        {
            if (idx < 0 || idx >= liveLandmarks.Count || idx >= clipLandmarks.Count)
                continue;

            var cp = clipLandmarks[idx];
            livePoints.Add(GetLivePointInClipSpace(liveLandmarks[idx]));
            clipPoints.Add(new Vector3(cp.x, cp.y, cp.z));
        }

        if (livePoints.Count == 0)
        {
            UpdateUI();
            return;
        }

        currentAccuracy = useProcrustesAlignment
            ? ComputeSimilarityAligned(livePoints, clipPoints)
            : ComputeSimilarityDirect(livePoints, clipPoints);

        UpdateUI();
    }

    void RefreshValidIndices()
    {
        if (referencePlayback != null && referencePlayback.RecordedIndices != null && referencePlayback.RecordedIndices.Length > 0)
        {
            validIndices = referencePlayback.RecordedIndices;
            Debug.Log($"AccuracyChecker: Using {validIndices.Length} recorded landmarks.");
            return;
        }

        validIndices = new int[33];
        for (int i = 0; i < 33; i++) validIndices[i] = i;
        Debug.LogWarning("AccuracyChecker: No recorded indices found, comparing all 33 landmarks.");
    }

    Vector3 GetLivePointInClipSpace(NormalizedLandmark lm)
    {
        float x = playerTracker.mirror ? 1f - lm.x : lm.x;
        if (legacyExtraMirrorLiveLandmarks) x = 1f - x;

        float y = playerTracker.flipY ? 1f - lm.y : lm.y;
        float z = lm.z * playerTracker.depthMultiplier + playerTracker.depthOffset;

        return new Vector3(x, y, z);
    }

    void UpdateUI()
    {
        if (accuracyText != null)
            accuracyText.text = $"Accuracy: {currentAccuracy:P0}";
    }

    float ComputeSimilarityDirect(List<Vector3> live, List<Vector3> clip)
    {
        float totalDist = 0f;
        for (int i = 0; i < live.Count; i++)
            totalDist += Vector3.Distance(live[i], clip[i]);

        float avgDist = totalDist / live.Count;
        return Mathf.Clamp01(1f - (avgDist / maxDistanceForZeroScore));
    }

    float ComputeSimilarityAligned(List<Vector3> live, List<Vector3> clip)
    {
        Vector3 centroidLive = Vector3.zero;
        Vector3 centroidClip = Vector3.zero;

        for (int i = 0; i < live.Count; i++)
        {
            centroidLive += live[i];
            centroidClip += clip[i];
        }

        centroidLive /= live.Count;
        centroidClip /= clip.Count;

        float totalDist = 0f;
        for (int i = 0; i < live.Count; i++)
        {
            Vector3 alignedClip = clip[i] - centroidClip + centroidLive;
            totalDist += Vector3.Distance(live[i], alignedClip);
        }

        float avgDist = totalDist / live.Count;
        return Mathf.Clamp01(1f - (avgDist / maxDistanceForZeroScore));
    }
}
