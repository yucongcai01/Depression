using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum GameActionOrderMode
{
    Fixed,
    RandomizedEachSession
}

public class GameSceneController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DancePlayback referencePlayback;
    [SerializeField] private AccuracyChecker accuracyChecker;
    [SerializeField] private AdaptiveDifficultyController adaptiveDifficultyController;
    [SerializeField] private FaceEmotionProvider faceEmotionProvider;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text countdownText;

    [Header("Mode Buttons")]
    [SerializeField] private Button noAdaptiveButton;
    [SerializeField] private Button physicalOnlyButton;
    [SerializeField] private Button emotionOnlyButton;
    [SerializeField] private Button physicalAndEmotionButton;

    [Header("Session")]
    [SerializeField] private bool playOnStart = false;
    [SerializeField] private float countdownSeconds = 3f;
    [SerializeField] private float actionDurationSeconds = 15f;
    [SerializeField] private GameActionOrderMode actionOrderMode = GameActionOrderMode.Fixed;
    [SerializeField] private AdaptiveDifficultyMode adaptiveModeOnStart = AdaptiveDifficultyMode.PhysicalAndEmotion;
    [SerializeField] private string waitingForModeText = "Select an adaptation mode to start.";

    [Header("Clips")]
    [SerializeField] private DanceClipData[] actionClips;
    [SerializeField] private string[] defaultClipResourcePaths =
    {
        "DanceClips/HipPop1",
        "DanceClips/HipPop2",
        "DanceClips/HipPop3",
        "DanceClips/HipPop4"
    };

    public bool IsRunning { get; private set; }
    public bool IsCountingDown { get; private set; }
    public int CurrentActionNumber { get; private set; }
    public string CurrentClipName { get; private set; } = string.Empty;
    public float CurrentActionRemainingSeconds { get; private set; }
    public float CountdownRemainingSeconds { get; private set; }
    public float ActionDurationSeconds => actionDurationSeconds;
    public AdaptiveDifficultyMode CurrentAdaptiveMode => adaptiveModeOnStart;
    public string CurrentModeLabel => GetModeLabel(adaptiveModeOnStart);
    public int TotalActions => CountConfiguredClips();

    private Coroutine sessionCoroutine;

    private void Awake()
    {
        ResolveReferences();
        LoadDefaultClipsIfNeeded();
        WireReferences();
        RegisterModeButtons();
        SetModeButtonsInteractable(true);
    }

    private void Start()
    {
        if (playOnStart)
            StartSession();
        else
            SetStatusText(waitingForModeText);
    }

    private void OnDestroy()
    {
        UnregisterModeButtons();
    }

    public void StartSession()
    {
        StartSession(adaptiveModeOnStart);
    }

    public void StartNoAdaptiveSession()
    {
        StartSession(AdaptiveDifficultyMode.NoAdaptive);
    }

    public void StartPhysicalOnlySession()
    {
        StartSession(AdaptiveDifficultyMode.PhysicalOnly);
    }

    public void StartEmotionOnlySession()
    {
        StartSession(AdaptiveDifficultyMode.EmotionOnly);
    }

    public void StartPhysicalAndEmotionSession()
    {
        StartSession(AdaptiveDifficultyMode.PhysicalAndEmotion);
    }

    public void StartSession(AdaptiveDifficultyMode adaptiveMode)
    {
        if (IsRunning)
            return;

        if (sessionCoroutine != null)
            StopCoroutine(sessionCoroutine);

        adaptiveModeOnStart = adaptiveMode;
        ResolveReferences();
        LoadDefaultClipsIfNeeded();
        WireReferences();
        SetModeButtonsInteractable(false);
        sessionCoroutine = StartCoroutine(RunSession());
    }

    public void StopSession()
    {
        if (sessionCoroutine != null)
        {
            StopCoroutine(sessionCoroutine);
            sessionCoroutine = null;
        }

        IsRunning = false;
        IsCountingDown = false;
        CurrentActionNumber = 0;
        CurrentClipName = string.Empty;
        CurrentActionRemainingSeconds = 0f;
        CountdownRemainingSeconds = 0f;

        if (referencePlayback != null)
            referencePlayback.Stop();

        SetCountdownText(string.Empty);
        SetStatusText(waitingForModeText);
        SetModeButtonsInteractable(true);
    }

    private IEnumerator RunSession()
    {
        IsRunning = true;
        IsCountingDown = false;
        CurrentActionNumber = 0;
        CurrentClipName = string.Empty;
        CurrentActionRemainingSeconds = 0f;
        CountdownRemainingSeconds = 0f;

        if (referencePlayback == null)
        {
            SetStatusText("Missing DancePlayback.");
            IsRunning = false;
            SetModeButtonsInteractable(true);
            yield break;
        }

        List<DanceClipData> clips = BuildClipSequence();
        if (clips.Count == 0)
        {
            SetStatusText("No HipPop clips configured.");
            IsRunning = false;
            SetModeButtonsInteractable(true);
            yield break;
        }

        referencePlayback.Stop();
        ConfigureAdaptiveController();

        yield return RunCountdown();

        for (int i = 0; i < clips.Count; i++)
        {
            CurrentActionNumber = i + 1;
            yield return PlayActionClip(clips[i], CurrentActionNumber, clips.Count);
        }

        referencePlayback.Stop();
        SetCountdownText(string.Empty);
        SetStatusText($"Session complete: {GetModeLabel(adaptiveModeOnStart)}. Select a mode for the next round.");
        CurrentActionNumber = 0;
        CurrentClipName = string.Empty;
        CurrentActionRemainingSeconds = 0f;
        CountdownRemainingSeconds = 0f;
        IsRunning = false;
        sessionCoroutine = null;
        SetModeButtonsInteractable(true);
    }

    private IEnumerator RunCountdown()
    {
        float remaining = Mathf.Max(0f, countdownSeconds);
        IsCountingDown = remaining > 0f;

        while (remaining > 0f)
        {
            CountdownRemainingSeconds = remaining;
            SetCountdownText(Mathf.CeilToInt(remaining).ToString());
            SetStatusText($"Get ready: {GetModeLabel(adaptiveModeOnStart)}.");
            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        CountdownRemainingSeconds = 0f;
        SetCountdownText("Start");
        yield return new WaitForSeconds(0.25f);
        SetCountdownText(string.Empty);
        IsCountingDown = false;
    }

    private IEnumerator PlayActionClip(DanceClipData clip, int actionNumber, int actionCount)
    {
        CurrentClipName = clip != null ? clip.name : string.Empty;
        CurrentActionRemainingSeconds = actionDurationSeconds;

        referencePlayback.Stop();
        referencePlayback.clip = clip;
        referencePlayback.loopCurrentClip = true;
        referencePlayback.Seek(0f);

        if (adaptiveDifficultyController != null)
            adaptiveDifficultyController.ResetAdaptiveStateForNewAction();

        referencePlayback.Play();

        float elapsed = 0f;
        while (elapsed < actionDurationSeconds)
        {
            float remaining = Mathf.Max(0f, actionDurationSeconds - elapsed);
            CurrentActionRemainingSeconds = remaining;
            SetStatusText($"{GetModeLabel(adaptiveModeOnStart)} | Action {actionNumber}/{actionCount}: {clip.name}  {Mathf.CeilToInt(remaining)}s");

            elapsed += Time.deltaTime;
            yield return null;
        }

        CurrentActionRemainingSeconds = 0f;
        referencePlayback.Pause();
    }

    private void ResolveReferences()
    {
        if (referencePlayback == null)
            referencePlayback = FindAnyObjectByType<DancePlayback>();

        if (accuracyChecker == null)
            accuracyChecker = FindAnyObjectByType<AccuracyChecker>();

        if (adaptiveDifficultyController == null)
            adaptiveDifficultyController = FindAnyObjectByType<AdaptiveDifficultyController>();

        if (faceEmotionProvider == null)
            faceEmotionProvider = FindAnyObjectByType<FaceEmotionProvider>();
    }

    private void WireReferences()
    {
        if (accuracyChecker != null && referencePlayback != null && accuracyChecker.referencePlayback == null)
            accuracyChecker.referencePlayback = referencePlayback;

        if (adaptiveDifficultyController == null)
            return;

        if (adaptiveDifficultyController.dancePlayback == null)
            adaptiveDifficultyController.dancePlayback = referencePlayback;

        if (adaptiveDifficultyController.accuracyChecker == null)
            adaptiveDifficultyController.accuracyChecker = accuracyChecker;

        if (adaptiveDifficultyController.faceEmotionProvider == null)
            adaptiveDifficultyController.faceEmotionProvider = faceEmotionProvider;
    }

    private void ConfigureAdaptiveController()
    {
        if (adaptiveDifficultyController == null)
            return;

        adaptiveDifficultyController.SetAdaptiveMode(adaptiveModeOnStart);
    }

    private void RegisterModeButtons()
    {
        UnregisterModeButtons();

        if (noAdaptiveButton != null)
            noAdaptiveButton.onClick.AddListener(StartNoAdaptiveSession);

        if (physicalOnlyButton != null)
            physicalOnlyButton.onClick.AddListener(StartPhysicalOnlySession);

        if (emotionOnlyButton != null)
            emotionOnlyButton.onClick.AddListener(StartEmotionOnlySession);

        if (physicalAndEmotionButton != null)
            physicalAndEmotionButton.onClick.AddListener(StartPhysicalAndEmotionSession);
    }

    private void UnregisterModeButtons()
    {
        if (noAdaptiveButton != null)
            noAdaptiveButton.onClick.RemoveListener(StartNoAdaptiveSession);

        if (physicalOnlyButton != null)
            physicalOnlyButton.onClick.RemoveListener(StartPhysicalOnlySession);

        if (emotionOnlyButton != null)
            emotionOnlyButton.onClick.RemoveListener(StartEmotionOnlySession);

        if (physicalAndEmotionButton != null)
            physicalAndEmotionButton.onClick.RemoveListener(StartPhysicalAndEmotionSession);
    }

    private void SetModeButtonsInteractable(bool interactable)
    {
        if (noAdaptiveButton != null)
            noAdaptiveButton.interactable = interactable;

        if (physicalOnlyButton != null)
            physicalOnlyButton.interactable = interactable;

        if (emotionOnlyButton != null)
            emotionOnlyButton.interactable = interactable;

        if (physicalAndEmotionButton != null)
            physicalAndEmotionButton.interactable = interactable;
    }

    private void LoadDefaultClipsIfNeeded()
    {
        if (HasConfiguredClips())
            return;

        if (defaultClipResourcePaths == null || defaultClipResourcePaths.Length == 0)
            return;

        List<DanceClipData> loadedClips = new List<DanceClipData>();
        foreach (string resourcePath in defaultClipResourcePaths)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                continue;

            DanceClipData clip = Resources.Load<DanceClipData>(NormalizeResourcesPath(resourcePath));
            if (clip != null)
                loadedClips.Add(clip);
            else
                Debug.LogWarning($"GameSceneController: Could not load clip at Resources/{resourcePath}.");
        }

        actionClips = loadedClips.ToArray();
    }

    private List<DanceClipData> BuildClipSequence()
    {
        List<DanceClipData> clips = new List<DanceClipData>();

        if (actionClips == null)
            return clips;

        foreach (DanceClipData clip in actionClips)
        {
            if (clip != null)
                clips.Add(clip);
        }

        if (actionOrderMode == GameActionOrderMode.RandomizedEachSession)
            Shuffle(clips);

        return clips;
    }

    private void Shuffle(List<DanceClipData> clips)
    {
        for (int i = clips.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            DanceClipData clip = clips[i];
            clips[i] = clips[swapIndex];
            clips[swapIndex] = clip;
        }
    }

    private bool HasConfiguredClips()
    {
        if (actionClips == null)
            return false;

        foreach (DanceClipData clip in actionClips)
        {
            if (clip != null)
                return true;
        }

        return false;
    }

    private int CountConfiguredClips()
    {
        int count = 0;

        if (actionClips == null)
            return count;

        foreach (DanceClipData clip in actionClips)
        {
            if (clip != null)
                count++;
        }

        return count;
    }

    private string NormalizeResourcesPath(string resourcePath)
    {
        resourcePath = resourcePath.Replace("\\", "/").Trim();

        const string resourcesPrefix = "Resources/";
        int resourcesIndex = resourcePath.IndexOf(resourcesPrefix, System.StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex >= 0)
            resourcePath = resourcePath.Substring(resourcesIndex + resourcesPrefix.Length);

        if (resourcePath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            resourcePath = resourcePath.Substring(0, resourcePath.Length - ".asset".Length);

        return resourcePath;
    }

    private void SetStatusText(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void SetCountdownText(string message)
    {
        if (countdownText != null)
            countdownText.text = message;
    }

    private string GetModeLabel(AdaptiveDifficultyMode adaptiveMode)
    {
        switch (adaptiveMode)
        {
            case AdaptiveDifficultyMode.NoAdaptive:
                return "No Adaptive";
            case AdaptiveDifficultyMode.PhysicalOnly:
                return "Physical Only";
            case AdaptiveDifficultyMode.EmotionOnly:
                return "Emotion Only";
            case AdaptiveDifficultyMode.PhysicalAndEmotion:
                return "Physical + Emotion";
            default:
                return adaptiveMode.ToString();
        }
    }
}
