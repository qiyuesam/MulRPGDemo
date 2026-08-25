using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuestEntryUI : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button selectButton;
    [SerializeField] private Color normalColor = new Color(0.10f, 0.12f, 0.16f, 0.98f);
    [SerializeField] private Color selectedColor = new Color(0.18f, 0.34f, 0.52f, 1f);

    public void Configure(string title, string status, Action onSelected)
    {
        if (titleText != null)
            titleText.text = title ?? string.Empty;

        if (statusText != null)
            statusText.text = status ?? string.Empty;

        if (selectButton == null)
            return;

        selectButton.onClick.RemoveAllListeners();
        selectButton.interactable = onSelected != null;

        if (onSelected != null)
            selectButton.onClick.AddListener(() => onSelected());
    }

    public void SetSelected(bool selected)
    {
        if (background != null)
            background.color = selected ? selectedColor : normalColor;
    }

    private void OnDestroy()
    {
        if (selectButton != null)
            selectButton.onClick.RemoveAllListeners();
    }
}

