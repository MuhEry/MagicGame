public struct DecisionResult
{
    public int playerIndex;
    public int itemId;
    public ItemCategory correct;
    public ItemCategory chosen;
    public bool isCorrect;
    public float inspectMs;
    public string explanation;
    public int shakeCount;
}
