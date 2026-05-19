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

    [Header("Skill Experience Inputs")]
    [SerializeField] private TMP_InputField exergameExperienceInput;
    [SerializeField] private TMP_InputField danceGameExperienceInput;
    [SerializeField] private bool autoCreateSkillExperienceQuestions = true;
    [SerializeField] private string exergameExperienceQuestion = "5. I have played motion-based or exergames before.";
    [SerializeField] private string danceGameExperienceQuestion = "6. I have played dance or rhythm movement games before.";

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
        EnsureSkillExperienceInputs();

        RegisterInput(moodInput);
        RegisterInput(interestInput);
        RegisterInput(tensionInput);
        RegisterInput(fatigueInput);
        RegisterInput(exergameExperienceInput);
        RegisterInput(danceGameExperienceInput);

        if (continueButton != null)
            continueButton.onClick.AddListener(Submit);
    }

    private void OnDestroy()
    {
        UnregisterInput(moodInput);
        UnregisterInput(interestInput);
        UnregisterInput(tensionInput);
        UnregisterInput(fatigueInput);
        UnregisterInput(exergameExperienceInput);
        UnregisterInput(danceGameExperienceInput);

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
                : $"Please answer all six questions with a number from {minimumAnswer} to {maximumAnswer}.";
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
        if (!TryReadAnswer(exergameExperienceInput, out data.exergameExperience)) return false;
        if (!TryReadAnswer(danceGameExperienceInput, out data.danceGameExperience)) return false;

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

    private void EnsureSkillExperienceInputs()
    {
        if (!autoCreateSkillExperienceQuestions)
            return;

        Transform parent = fatigueInput != null ? fatigueInput.transform.parent : transform;

        if (exergameExperienceInput == null)
            exergameExperienceInput = CreateSkillExperienceQuestion(
                parent,
                "Q5",
                "A5",
                exergameExperienceQuestion,
                questionPosition: new Vector2(0f, -210f),
                inputPosition: new Vector2(784f, -218f));

        if (danceGameExperienceInput == null)
            danceGameExperienceInput = CreateSkillExperienceQuestion(
                parent,
                "Q6",
                "A6",
                danceGameExperienceQuestion,
                questionPosition: new Vector2(0f, -282f),
                inputPosition: new Vector2(784f, -290f));
    }

    private TMP_InputField CreateSkillExperienceQuestion(
        Transform parent,
        string questionName,
        string inputName,
        string question,
        Vector2 questionPosition,
        Vector2 inputPosition)
    {
        TMP_Text questionText = CreateQuestionText(parent, questionName, question, questionPosition);
        TMP_InputField input = CreateQuestionInput(parent, inputName, inputPosition);

        if (questionText != null)
            questionText.transform.SetAsLastSibling();

        if (input != null)
            input.transform.SetAsLastSibling();

        return input;
    }

    private TMP_Text CreateQuestionText(Transform parent, string objectName, string question, Vector2 anchoredPosition)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(1200f, 50f);

        TMP_Text text = textObject.GetComponent<TMP_Text>();
        CopyTextStyle(GetQuestionTextTemplate(), text);
        text.text = question;
        return text;
    }

    private TMP_InputField CreateQuestionInput(Transform parent, string objectName, Vector2 anchoredPosition)
    {
        if (fatigueInput == null)
            return null;

        TMP_InputField input = Instantiate(fatigueInput, parent);
        input.name = objectName;
        input.text = string.Empty;

        RectTransform rectTransform = input.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(160f, 30f);

        return input;
    }

    private TMP_Text GetQuestionTextTemplate()
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name.StartsWith("Q"))
                return text;
        }

        return statusText;
    }

    private void CopyTextStyle(TMP_Text source, TMP_Text destination)
    {
        if (source == null || destination == null)
            return;

        destination.font = source.font;
        destination.fontSharedMaterial = source.fontSharedMaterial;
        destination.color = source.color;
        destination.fontSize = source.fontSize;
        destination.alignment = source.alignment;
        destination.textWrappingMode = source.textWrappingMode;
        destination.fontStyle = source.fontStyle;
    }
}
