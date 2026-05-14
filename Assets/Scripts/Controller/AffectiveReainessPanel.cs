using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AffectiveReainessPanel : MonoBehaviour
{
    [Header("Question Inputs")]
    [SerializeField] private TMP_InputField moodInput;
    [SerializeField] private TMP_InputField interestInput;
    [SerializeField] private TMP_InputField tensionInput;
    [SerializeField] private TMP_InputField fatigueInput;

    [Header("Controls")]
    [SerializeField] private Button continueButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Answer Range")]
    [SerializeField] private int minimumAnswer = 1;
    [SerializeField] private int maximumAnswer = 5;

    private CalibrationSceneController sceneController;

    public bool CanContinue => TryReadAnswers(out _);

    public void Initialize(CalibrationSceneController controller)
    {
        sceneController = controller;
        RefreshContinueState();
    }

    private void Awake()
    {
        RegisterInput(moodInput);
        RegisterInput(interestInput);
        RegisterInput(tensionInput);
        RegisterInput(fatigueInput);

        if (continueButton != null)
            continueButton.onClick.AddListener(Submit);
    }

    private void OnDestroy()
    {
        UnregisterInput(moodInput);
        UnregisterInput(interestInput);
        UnregisterInput(tensionInput);
        UnregisterInput(fatigueInput);

        if (continueButton != null)
            continueButton.onClick.RemoveListener(Submit);
    }

    private void OnEnable()
    {
        RefreshContinueState();
    }

    public CalibrationAffectiveReadinessData GetData()
    {
        TryReadAnswers(out CalibrationAffectiveReadinessData data);
        return data;
    }

    public void Submit()
    {
        if (!TryReadAnswers(out _))
        {
            RefreshContinueState();
            return;
        }

        if (sceneController != null)
            sceneController.ContinueFromAffectiveReadiness();
    }

    public void RefreshContinueState()
    {
        bool ready = TryReadAnswers(out _);

        if (continueButton != null)
            continueButton.interactable = ready;

        if (statusText != null)
        {
            statusText.text = ready
                ? string.Empty
                : $"Please answer all four questions with a number from {minimumAnswer} to {maximumAnswer}.";
        }
    }

    private void RegisterInput(TMP_InputField input)
    {
        if (input != null)
            input.onValueChanged.AddListener(HandleInputChanged);
    }

    private void UnregisterInput(TMP_InputField input)
    {
        if (input != null)
            input.onValueChanged.RemoveListener(HandleInputChanged);
    }

    private void HandleInputChanged(string value)
    {
        RefreshContinueState();
    }

    private bool TryReadAnswers(out CalibrationAffectiveReadinessData data)
    {
        data = new CalibrationAffectiveReadinessData();

        if (!TryReadAnswer(moodInput, out data.mood)) return false;
        if (!TryReadAnswer(interestInput, out data.interest)) return false;
        if (!TryReadAnswer(tensionInput, out data.tension)) return false;
        if (!TryReadAnswer(fatigueInput, out data.fatigue)) return false;

        return true;
    }

    private bool TryReadAnswer(TMP_InputField input, out int value)
    {
        value = 0;

        if (input == null)
            return false;

        if (!int.TryParse(input.text, out value))
            return false;

        return value >= minimumAnswer && value <= maximumAnswer;
    }
}
