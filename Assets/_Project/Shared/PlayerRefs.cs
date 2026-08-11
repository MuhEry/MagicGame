using UnityEngine;

/// <summary>
/// Yerel oyuncunun rig referanslari (mimari kural 8).
///
/// Camera.main DOGRUDAN cagrilmaz: Faz 2'de sahnede iki avatar bulunur ve
/// "ana kamera" belirsizlesir. Kamerayi ve elleri isteyen herkes buradan alir.
///
/// KONUM KURALI: Bu bilesen XR rig'in ALTINA konmaz, "Systems" altinda durur.
/// Alteruna'nin avatar temizligi rig altindaki her Behaviour icin
/// type.Namespace.Length okur; bu projedeki scriptler global namespace'te
/// oldugu icin Namespace NULL doner ve temizlik NullReferenceException ile yarida kalir.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Kayip Esya/Player Refs")]
public class PlayerRefs : MonoBehaviour
{
    public static PlayerRefs Instance { get; private set; }

    [Tooltip("Yerel XR rig'in kamerasi. Bos birakilirsa bir kez Camera.main'den cozulur.")]
    [SerializeField] private Camera mainCamera;

    [Tooltip("Sol el / kontrolcu transformu. Faz 2 avatari bunu takip eder.")]
    [SerializeField] private Transform leftHand;

    [Tooltip("Sag el / kontrolcu transformu. Faz 2 avatari bunu takip eder.")]
    [SerializeField] private Transform rightHand;

    public Camera MainCamera => mainCamera;
    public Transform Head => mainCamera != null ? mainCamera.transform : null;
    public Transform LeftHand => leftHand;
    public Transform RightHand => rightHand;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveMissingReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Inspector'da baglanmamis alanlari sahneden cozer. Kurulum komutu bunlari
    /// zaten baglar; burasi yalnizca elle kurulan sahneler icin emniyet agidir.
    /// </summary>
    private void ResolveMissingReferences()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (leftHand != null && rightHand != null)
            return;

        FindHandTransforms(out Transform foundLeft, out Transform foundRight);

        if (leftHand == null)
            leftHand = foundLeft;

        if (rightHand == null)
            rightHand = foundRight;
    }

    // El kokleri en iyiden en kotuye. Ilk esleseni degil, EN IYI eslesen kazanir.
    static readonly string[] k_LeftNames = { "left hand", "lefthand", "left controller", "left" };
    static readonly string[] k_RightNames = { "right hand", "righthand", "right controller", "right" };

    /// <summary>
    /// XR rig'in sol/sag el koklerini sahneden bulur.
    ///
    /// DIKKAT - INTERACTOR ADINA BAKMAYIN: XRI 3.x Hands rig'inde interactor'larin
    /// adi "Poke Interactor", "Near-Far Interactor" gibidir; el secimi onlarin
    /// adinda GECMEZ. El secimi hiyerarside, bir ust seviyedeki "Left Hand" /
    /// "Right Hand" objesindedir. Bu yuzden tum transformlari tariyor ve ad
    /// benzerligine gore PUANLIYORUZ.
    ///
    /// Editor kurulum komutu da bu metodu cagirir; tek bir dogru uygulama olsun diye
    /// burada public static duruyor.
    /// </summary>
    public static void FindHandTransforms(out Transform left, out Transform right)
    {
        left = null;
        right = null;

        int leftScore = int.MaxValue;
        int rightScore = int.MaxValue;

        foreach (Transform candidate in FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate == null)
                continue;

            string name = candidate.name.ToLowerInvariant();

            int score = Score(name, k_LeftNames);
            if (score < leftScore)
            {
                leftScore = score;
                left = candidate;
            }

            score = Score(name, k_RightNames);
            if (score < rightScore)
            {
                rightScore = score;
                right = candidate;
            }
        }
    }

    /// <summary>Kucuk puan = daha iyi eslesme. int.MaxValue = hic eslesmedi.</summary>
    static int Score(string name, string[] candidates)
    {
        for (int index = 0; index < candidates.Length; index++)
        {
            if (name.Contains(candidates[index]))
                return index;
        }

        return int.MaxValue;
    }
}
