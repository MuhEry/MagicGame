using System;
using UnityEngine;

public class ShiftManager : MonoBehaviour
{
    public static ShiftManager Instance;

    public event Action<DecisionResult> OnDecision;
    public event Action<float> OnTimeChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void RegisterDecision(
        int itemId,
        ItemCategory correct,
        ItemCategory chosen,
        float inspectMs,
        int shakeCount)
    {
        bool isCorrect = correct == chosen;

        DecisionResult result = new DecisionResult
        {
            itemId = itemId,
            correct = correct,
            chosen = chosen,
            isCorrect = isCorrect,
            inspectMs = inspectMs,
            explanation = isCorrect
                ? "Eşya doğru dolaba bırakıldı."
                : "Eşya yanlış dolaba bırakıldı."
        };

        OnDecision?.Invoke(result);
    }
}