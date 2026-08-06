/// <summary>
/// Tek bir yerlestirme kararinin sonucu. ShiftManager.OnDecision event'i ile
/// yayinlanir, TelemetryLogger bunu CSV satirina cevirir.
///
/// SARTNAME NOTU: Alanlar sartnamedeki Gelistirici C blogundan birebir alinmistir:
///   public struct DecisionResult { itemId, correct, chosen, isCorrect, inspectMs, explanation }
/// Bu dosya ortak sozlesmedir - ekip kendi surumunu push ederse onlarinki alinir.
/// </summary>
public struct DecisionResult
{
    /// <summary>Esyanin kalici kimligi (ItemIdentity uzerindeki int id).</summary>
    public int itemId;

    /// <summary>Esyanin gercek kategorisi.</summary>
    public ItemCategory correct;

    /// <summary>Oyuncunun sectigi dolabin kategorisi.</summary>
    public ItemCategory chosen;

    /// <summary>correct == chosen mi?</summary>
    public bool isCorrect;

    /// <summary>Esyanin incelenmesine harcanan sure (milisaniye).</summary>
    public float inspectMs;

    /// <summary>Panoda gosterilen mikro-aciklama. Orn: "Bu esya sallandiginda ses cikariyordu."</summary>
    public string explanation;
}
