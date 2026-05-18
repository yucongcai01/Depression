using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Unity.Sample.FaceLandmarkDetection;
using TMPro;
using UnityEngine;

public class FaceEmotionProvider : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FaceLandmarkerRunner faceLandmarkerRunner;
    [SerializeField] private TMP_Text debugText;

    [Header("Smoothing")]
    [Tooltip("Seconds used by the exponential moving average. Around 1 second is stable enough for DDA.")]
    [SerializeField] private float smoothingWindowSeconds = 1f;
    [Tooltip("How long to keep the last values after the face disappears.")]
    [SerializeField] private float faceLostGraceSeconds = 0.5f;

    [Header("Output (Read Only)")]
    [Range(-1f, 1f)] public float valenceProxy;
    [Range(0f, 1f)] public float arousalProxy;
    [Range(-1f, 1f)] public float smoothedValence;
    [Range(0f, 1f)] public float smoothedArousal;
    public bool faceDetected;
    public float lastFaceSeenTime;

    public IReadOnlyDictionary<string, float> BlendshapeScores => blendshapeScores;

    private readonly Dictionary<string, float> blendshapeScores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private bool hasSmoothedValue;

    private void Awake()
    {
        if (faceLandmarkerRunner == null)
            faceLandmarkerRunner = FindFirstObjectByType<FaceLandmarkerRunner>();
    }

    private void Update()
    {
        if (faceLandmarkerRunner == null)
        {
            SetFaceLost();
            UpdateDebugText();
            return;
        }

        if (!faceLandmarkerRunner.TryGetLatestResult(out FaceLandmarkerResult result) ||
            result.faceBlendshapes == null ||
            result.faceBlendshapes.Count == 0)
        {
            SetFaceLost();
            UpdateDebugText();
            return;
        }

        Classifications firstFaceBlendshapes = result.faceBlendshapes[0];
        if (firstFaceBlendshapes.categories == null || firstFaceBlendshapes.categories.Count == 0)
        {
            SetFaceLost();
            UpdateDebugText();
            return;
        }

        faceDetected = true;
        lastFaceSeenTime = Time.time;
        CacheBlendshapes(firstFaceBlendshapes.categories);

        float smile = AverageScore("mouthSmileLeft", "mouthSmileRight");
        float frown = AverageScore("mouthFrownLeft", "mouthFrownRight");
        float browDown = AverageScore("browDownLeft", "browDownRight");
        float eyeWide = AverageScore("eyeWideLeft", "eyeWideRight");
        float jawOpen = GetScore("jawOpen");
        float browInnerUp = GetScore("browInnerUp");

        valenceProxy = Mathf.Clamp(smile - frown - browDown, -1f, 1f);
        arousalProxy = Mathf.Clamp01((jawOpen + eyeWide + browInnerUp) / 3f);

        SmoothOutputs();
        UpdateDebugText();
    }

    public bool TryGetAffectiveState(out float valence, out float arousal)
    {
        bool hasRecentFace = faceDetected || Time.time - lastFaceSeenTime <= faceLostGraceSeconds;
        valence = smoothedValence;
        arousal = smoothedArousal;
        return hasRecentFace && hasSmoothedValue;
    }

    public float GetScore(string blendshapeName)
    {
        return blendshapeScores.TryGetValue(blendshapeName, out float score) ? score : 0f;
    }

    private void CacheBlendshapes(List<Category> categories)
    {
        blendshapeScores.Clear();

        int count = categories.Count;
        for (int i = 0; i < count; i++)
        {
            Category category = categories[i];
            string name = !string.IsNullOrEmpty(category.categoryName)
                ? category.categoryName
                : category.displayName;

            if (!string.IsNullOrEmpty(name))
                blendshapeScores[name] = Mathf.Clamp01(category.score);
        }
    }

    private float AverageScore(string leftBlendshape, string rightBlendshape)
    {
        return (GetScore(leftBlendshape) + GetScore(rightBlendshape)) * 0.5f;
    }

    private void SmoothOutputs()
    {
        if (!hasSmoothedValue)
        {
            smoothedValence = valenceProxy;
            smoothedArousal = arousalProxy;
            hasSmoothedValue = true;
            return;
        }

        float window = Mathf.Max(0.01f, smoothingWindowSeconds);
        float alpha = 1f - Mathf.Exp(-Time.deltaTime / window);
        smoothedValence = Mathf.Lerp(smoothedValence, valenceProxy, alpha);
        smoothedArousal = Mathf.Lerp(smoothedArousal, arousalProxy, alpha);
    }

    private void SetFaceLost()
    {
        faceDetected = Time.time - lastFaceSeenTime <= faceLostGraceSeconds;

        if (!faceDetected && !hasSmoothedValue)
        {
            valenceProxy = 0f;
            arousalProxy = 0f;
            smoothedValence = 0f;
            smoothedArousal = 0f;
        }
    }

    private void UpdateDebugText()
    {
        if (debugText == null)
            return;

        debugText.text =
            $"Face: {(faceDetected ? "Detected" : "Lost")}\n" +
            $"Valence: {smoothedValence:F2}\n" +
            $"Arousal: {smoothedArousal:F2}\n" +
            $"Smile: {AverageScore("mouthSmileLeft", "mouthSmileRight"):F2}\n" +
            $"Frown: {AverageScore("mouthFrownLeft", "mouthFrownRight"):F2}\n" +
            $"Brow Down: {AverageScore("browDownLeft", "browDownRight"):F2}\n" +
            $"Jaw Open: {GetScore("jawOpen"):F2}";
    }
}
